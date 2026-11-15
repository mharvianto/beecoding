using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[Authorize]
[Route("api/boards/{slug}/problems")]
public class ProblemsController(AppDbContext db, BoardService boards, VisibilityService vis,
    IBoardNotifier notifier, AdminAccess admin, AuditLog audit, NotificationService notifications) : ApiControllerBase
{
    private readonly NotificationService _notifications = notifications;
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly VisibilityService _vis = vis;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly AdminAccess _admin = admin;
    private readonly AuditLog _audit = audit;

    [HttpGet]
    public async Task<ActionResult<object>> List(string slug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var me = await _boards.GetMembershipAsync(boardId.Value, UserId);
        bool isAdmin = IsAdminUser(_admin);
        if (me is null && !isAdmin) return Forbid();

        var problems = await _db.Problems
            .Where(p => p.BoardId == boardId.Value)
            // The list only needs the sample tests (hidden tests can be megabytes of stdin/expected output and are
            // returned by the single-problem GET the editor uses), so don't even load them from the database.
            .Include(p => p.TestCases.Where(t => t.IsSample))
            .Include(p => p.Group)
            .OrderBy(p => p.Position).ThenBy(p => p.Id)
            .ToListAsync();

        bool staff = me is not null ? _vis.IsStaff(me.Role) : isAdmin;
        if (staff) return problems.Select(Mapping.ToOwnerDto).ToList();

        var now = DateTime.UtcNow;
        var boardExam = await _db.Boards.Where(b => b.Id == boardId.Value).Select(b => b.ExamMode).FirstAsync();
        return problems.Where(p => GroupAccess.StudentVisible(p, now))
            .Select(p => Mapping.ToStudentDto(p, p.Group?.ExamMode ?? boardExam, GroupAccess.IsClosed(p, now))).ToList();
    }

    [HttpGet("{problemSlug}")]
    public async Task<ActionResult<object>> Get(string slug, string problemSlug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var me = await _boards.GetMembershipAsync(boardId.Value, UserId);
        if (me is null && !IsAdminUser(_admin)) return Forbid();

        var p = await _db.Problems
            .Include(x => x.TestCases)
            .Include(x => x.Group)
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId.Value);
        if (p is null) return NotFound();

        bool staff = me is not null ? _vis.IsStaff(me.Role) : IsAdminUser(_admin);
        var now = DateTime.UtcNow;
        if (!staff && !GroupAccess.StudentVisible(p, now)) return NotFound();
        if (staff) return Mapping.ToOwnerDto(p);
        var boardExam = await _db.Boards.Where(b => b.Id == boardId.Value).Select(b => b.ExamMode).FirstAsync();
        return Mapping.ToStudentDto(p, p.Group?.ExamMode ?? boardExam, GroupAccess.IsClosed(p, now));
    }

    /// <summary>Owner-only: hide/unhide a problem from students (draft/not-ready), without
    /// touching its data or existing submissions — see Problem.Hidden.</summary>
    [HttpPatch("{problemSlug}/hidden")]
    public async Task<ActionResult<ProblemDto>> SetHidden(string slug, string problemSlug, UpdateProblemVisibilityDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        // No test cases loaded: this only flips a flag, so the response carries an empty testCases list instead of
        // echoing every (possibly megabyte-sized) test back. Callers read groupId / hidden from it.
        var p = await _db.Problems
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId!.Value);
        if (p is null) return NotFound();

        var wasHidden = p.Hidden;
        p.Hidden = dto.Hidden;
        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(boardId!.Value);
        if (wasHidden && !p.Hidden) await _notifications.ProblemPublishedAsync(p);
        return Mapping.ToOwnerDto(p);
    }

    /// <summary>Owner-only: move a problem into a group (or out of all groups with a null id).</summary>
    [HttpPatch("{problemSlug}/group")]
    public async Task<ActionResult<ProblemDto>> SetGroup(string slug, string problemSlug, MoveProblemDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        // No test cases loaded: this only flips a flag, so the response carries an empty testCases list instead of
        // echoing every (possibly megabyte-sized) test back. Callers read groupId / hidden from it.
        var p = await _db.Problems
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId!.Value);
        if (p is null) return NotFound();
        if (dto.GroupId is int gid && !await _db.ProblemGroups.AnyAsync(g => g.Id == gid && g.BoardId == boardId!.Value))
            return BadRequest("Unknown group.");

        p.GroupId = dto.GroupId;
        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(boardId!.Value);
        return Mapping.ToOwnerDto(p);
    }

    /// <summary>Staff-only per-problem submission aggregate for the Problems page — computed
    /// straight from Submissions rather than the visibility-filtered progress grid, so a
    /// hidden problem's counts aren't zeroed out (Problem.Hidden only affects student-facing
    /// visibility, not staff-facing stats).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<List<ProblemStatDto>>> Stats(string slug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var me = await _boards.GetMembershipAsync(boardId.Value, UserId);
        bool isAdmin = IsAdminUser(_admin);
        if (!isAdmin && (me is null || me.Role == MembershipRole.Student)) return Forbid();

        var subs = await _db.Submissions
            .Where(s => s.Problem!.BoardId == boardId.Value)
            .Select(s => new { s.ProblemId, s.UserId, s.Verdict, s.Score })
            .ToListAsync();

        return subs.GroupBy(s => s.ProblemId).Select(g =>
        {
            var accepted = g.Count(s => s.Verdict == Verdict.Accepted && s.Score >= 1.0);
            var solved = g.Where(s => s.Verdict == Verdict.Accepted && s.Score >= 1.0)
                .Select(s => s.UserId).Distinct().Count();
            return new ProblemStatDto(g.Key, g.Count(), accepted, accepted / (double)g.Count(), solved);
        }).ToList();
    }

    /// <summary>Owner-only: persist a new top-to-bottom order (drag-and-drop on the problem list).</summary>
    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(string slug, ReorderProblemsDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var bySlug = await _db.Problems.Where(p => p.BoardId == boardId!.Value).ToDictionaryAsync(p => p.Slug);
        for (var i = 0; i < dto.Order.Count; i++)
            if (bySlug.TryGetValue(dto.Order[i], out var p)) p.Position = i;

        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(boardId!.Value);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<ProblemDto>> Create(string slug, UpsertProblemDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        if (ProblemRules.Validate(dto.Title, dto.StatementMarkdown, dto.TestCases, testsRequired: true) is { } invalid) return BadRequest(invalid);
        var p = new Problem { BoardId = boardId!.Value };
        Apply(p, dto);
        _db.Problems.Add(p);
        await _db.SaveChangesAsync();
        await AttachBankRowAsync(p);
        await _notifier.ProblemChangedAsync(boardId.Value);
        await _notifications.ProblemPublishedAsync(p);

        return Mapping.ToOwnerDto(p);
    }

    [HttpPut("{problemSlug}")]
    public async Task<ActionResult<ProblemDto>> Update(string slug, string problemSlug, UpsertProblemDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var p = await _db.Problems
            .Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId!.Value);
        if (p is null) return NotFound();
        if (ProblemRules.Validate(dto.Title, dto.StatementMarkdown, dto.TestCases, testsRequired: false) is { } invalid) return BadRequest(invalid);

        // Captured before Apply() adds any brand-new TestCase rows to p.TestCases — those
        // start with Id 0 (not yet persisted) and must never reach the removal filter below,
        // or EF throws trying to mark a never-saved entity as Deleted.
        var existingIds = p.TestCases.Select(t => t.Id).ToHashSet();
        var wasHidden = p.Hidden;

        var gradedBefore = BoardBulkService.GradedContent(p);

        Apply(p, dto);

        var removed = new List<TestCase>();
        if (dto.TestCases is not null)
        {
            var keepIds = dto.TestCases.Where(t => t.Id is > 0).Select(t => t.Id!.Value).ToHashSet();
            removed = p.TestCases.Where(t => existingIds.Contains(t.Id) && !keepIds.Contains(t.Id)).ToList();
        }

        // Once a student has submitted, what the judge compares against must not change under them; a new version is the way.
        if (BoardBulkService.GradedContent(p, p.TestCases.Except(removed)) != gradedBefore
            && await _db.Submissions.AnyAsync(s => s.ProblemId == p.Id))
        {
            _db.ChangeTracker.Clear();
            return Conflict("Students have already submitted to this problem, so its statement, tests and limits are locked. Create a new version instead.");
        }

        _db.TestCases.RemoveRange(removed);
        await _db.SaveChangesAsync();
        await SyncBankRowAsync(p);
        await _notifier.ProblemChangedAsync(boardId!.Value);
        if (wasHidden && !p.Hidden) await _notifications.ProblemPublishedAsync(p);

        var fresh = await _db.Problems.Include(x => x.TestCases).FirstAsync(x => x.Id == p.Id);
        return Mapping.ToOwnerDto(fresh);
    }

    /// <summary>
    /// A copy of the problem to edit once the original is locked: a hidden draft in the same place (its own bank row, same XP source),
    /// so nothing half-edited reaches students. The original is left as it is; hide it once the new one is published.
    /// </summary>
    [HttpPost("{problemSlug}/new-version")]
    public async Task<ActionResult<ProblemDto>> NewVersion(string slug, string problemSlug)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var old = await _db.Problems.Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId!.Value);
        if (old is null) return NotFound();

        var copy = new Problem
        {
            BoardId = old.BoardId, GroupId = old.GroupId, Position = old.Position, Hidden = true,
            Title = old.Title + " (new version)",
            StatementMarkdown = old.StatementMarkdown, AllowedLanguages = old.AllowedLanguages, Tags = old.Tags, Level = old.Level,
            GeneratedByAi = old.GeneratedByAi, BannedHeaders = old.BannedHeaders, BannedSymbols = old.BannedSymbols,
            InputFileName = old.InputFileName, TimeLimitMs = old.TimeLimitMs, MemoryLimitKb = old.MemoryLimitKb,
            SourceBankProblemId = old.SourceBankProblemId,
        };
        foreach (var t in old.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id))
            copy.TestCases.Add(new TestCase { Stdin = t.Stdin, ExpectedStdout = t.ExpectedStdout, IsSample = t.IsSample, Points = t.Points, Position = t.Position });
        _db.Problems.Add(copy);
        await _db.SaveChangesAsync();
        await AttachBankRowAsync(copy);
        await _notifier.ProblemChangedAsync(boardId!.Value);
        return Mapping.ToOwnerDto(copy);
    }

    /// <summary>Which copies on this board have fallen behind the bank problem they came from (owner only; tests, limits and statement compared).</summary>
    [HttpGet("bank-updates")]
    public async Task<ActionResult<List<BankUpdateDto>>> BankUpdates(string slug)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var copies = await _db.Problems.Include(x => x.TestCases)
            .Where(x => x.BoardId == boardId!.Value && x.SourceBankProblemId != null && !x.Hidden).ToListAsync();   // a hidden copy is not in use: hiding the old one after a draft clears its notice
        var sourceIds = copies.Select(x => x.SourceBankProblemId!.Value).Distinct().ToList();
        // A BoardOnly row is written through on every edit, so it can never be ahead; a deleted bank problem is filtered out and skipped.
        var banks = await _db.BankProblems.Include(b => b.TestCases).Where(b => sourceIds.Contains(b.Id) && !b.BoardOnly).Where(CanReadBank()).ToDictionaryAsync(b => b.Id);
        var behind = copies.Where(c => banks.TryGetValue(c.SourceBankProblemId!.Value, out var b)
            && BoardBulkService.GradedContent(c) != BoardBulkService.GradedContent(b)).ToList();
        var behindIds = behind.Select(c => c.Id).ToList();
        var locked = (await _db.Submissions.Where(s => behindIds.Contains(s.ProblemId)).Select(s => s.ProblemId).Distinct().ToListAsync()).ToHashSet();
        return behind.Select(c => new BankUpdateDto(c.Slug, locked.Contains(c.Id))).ToList();
    }

    /// <summary>
    /// Bring a copy up to date with its bank problem. Not yet submitted to: its content is replaced in place. Already submitted to: the bank
    /// version becomes a new hidden draft next to it (same rule as a locked edit), so what students were graded against stays as it was.
    /// </summary>
    [HttpPost("{problemSlug}/update-from-bank")]
    public async Task<ActionResult<ProblemDto>> UpdateFromBank(string slug, string problemSlug)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var p = await _db.Problems.Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId!.Value);
        if (p?.SourceBankProblemId is not int srcId) return NotFound();
        var bank = await _db.BankProblems.Include(b => b.TestCases).Where(CanReadBank()).FirstOrDefaultAsync(b => b.Id == srcId && !b.BoardOnly);
        if (bank is null) return NotFound();

        if (await _db.Submissions.AnyAsync(s => s.ProblemId == p.Id))
        {
            var copy = BoardBulkService.ProblemFromBank(bank, p.BoardId, p.Position);
            copy.GroupId = p.GroupId;
            copy.Hidden = true;
            copy.Title = bank.Title + " (updated)";
            _db.Problems.Add(copy);
            await _db.SaveChangesAsync();
            await _notifier.ProblemChangedAsync(boardId!.Value);
            return Mapping.ToOwnerDto(copy);
        }

        _db.TestCases.RemoveRange(p.TestCases.ToList());
        var fresh = BoardBulkService.ProblemFromBank(bank, p.BoardId, p.Position);
        p.Title = fresh.Title; p.StatementMarkdown = fresh.StatementMarkdown; p.AllowedLanguages = fresh.AllowedLanguages;
        p.Tags = fresh.Tags; p.Level = fresh.Level; p.BannedHeaders = fresh.BannedHeaders; p.BannedSymbols = fresh.BannedSymbols;
        p.InputFileName = fresh.InputFileName; p.TimeLimitMs = fresh.TimeLimitMs; p.MemoryLimitKb = fresh.MemoryLimitKb;
        foreach (var t in fresh.TestCases) p.TestCases.Add(t);
        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(boardId!.Value);
        return Mapping.ToOwnerDto(p);
    }

    /// <summary>What this teacher may read in the bank: their own, public ones, and ones shared with them. A copy only keeps getting
    /// updates from a problem they can still read (a revoked share stops them).</summary>
    private System.Linq.Expressions.Expression<Func<BankProblem, bool>> CanReadBank()
    {
        var me = UserId;
        return b => b.IsPublic || b.OwnerId == me || _db.BankProblemShares.Any(x => x.BankProblemId == b.Id && x.UserId == me);
    }

    /// <summary>The board problem's content of record: a private bank row. If this fails the problem simply has no link yet; the startup backfill adds it.</summary>
    private async Task AttachBankRowAsync(Problem p)
    {
        var bank = BoardBulkService.BankFromProblem(p, UserId);
        _db.BankProblems.Add(bank);
        await _db.SaveChangesAsync();
        p.BankProblemId = bank.Id;
        await _db.SaveChangesAsync();
    }

    /// <summary>Write an edit through to the bank row while that row exists only for this board (a bank problem the teacher shares keeps its own text).</summary>
    private async Task SyncBankRowAsync(Problem p)
    {
        if (p.BankProblemId is not int id) return;
        var bank = await _db.BankProblems.Include(b => b.TestCases).FirstOrDefaultAsync(b => b.Id == id && b.BoardOnly);
        if (bank is null) return;
        BoardBulkService.CopyContent(p, bank);
        await _db.SaveChangesAsync();
    }

    /// <summary>Soft-delete (owner or admin). Owner undo is time-boxed to
    /// <see cref="SoftDelete.UndoWindow"/>; an admin can restore any time.</summary>
    [HttpDelete("{problemSlug}")]
    public async Task<IActionResult> Delete(string slug, string problemSlug)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        if (board.OwnerId != UserId && !IsAdminUser(_admin)) return Forbid();

        var p = await _db.Problems.FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == board.Id);
        if (p is null) return NotFound();

        p.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "Problem", p.Id, p.Title);
        await _notifier.ProblemChangedAsync(board.Id);
        return NoContent();
    }

    [HttpPost("{problemSlug}/restore")]
    public async Task<IActionResult> Restore(string slug, string problemSlug)
    {
        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        bool isAdmin = IsAdminUser(_admin);
        if (board.OwnerId != UserId && !isAdmin) return Forbid();

        var p = await _db.Problems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == board.Id);
        if (p is null) return NotFound();
        if (!isAdmin && !SoftDelete.CanRestore(p.DeletedAt)) return StatusCode(StatusCodes.Status410Gone, "The undo window has expired.");

        p.DeletedAt = null;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "restore", "Problem", p.Id, p.Title);
        await _notifier.ProblemChangedAsync(board.Id);
        return NoContent();
    }

    private void Apply(Problem p, UpsertProblemDto dto)
    {
        p.Title = (dto.Title ?? "").Trim();
        p.StatementMarkdown = dto.StatementMarkdown ?? "";
        p.AllowedLanguages = Languages.Normalize(dto.AllowedLanguages);
        p.Tags = Mapping.NormalizeTags(dto.Tags);
        p.Level = Mapping.ParseLevel(dto.Level);
        p.BannedHeaders = SourcePolicy.Normalize(dto.BannedHeaders);
        p.BannedSymbols = SourcePolicy.NormalizeSymbols(dto.BannedSymbols);
        p.InputFileName = InputFilePolicy.Normalize(dto.InputFileName);
        p.TimeLimitMs = Math.Clamp(dto.TimeLimitMs <= 0 ? 1000 : dto.TimeLimitMs, 100, 10_000);
        p.MemoryLimitKb = Math.Clamp(dto.MemoryLimitKb <= 0 ? 32_768 : dto.MemoryLimitKb, 4_096, 512_000);
        p.Position = dto.Position;

        foreach (var t in dto.TestCases ?? Enumerable.Empty<UpsertTestCaseDto>())
        {
            var tc = t.Id is > 0 ? p.TestCases.FirstOrDefault(x => x.Id == t.Id) : null;
            if (tc is null)
            {
                tc = new TestCase { Problem = p };
                p.TestCases.Add(tc);
            }
            tc.Stdin = t.Stdin ?? "";
            tc.ExpectedStdout = t.ExpectedStdout ?? "";
            tc.IsSample = t.IsSample;
            tc.Points = Math.Max(0, t.Points);
            tc.Position = t.Position;
        }
    }

    private async Task<(int? boardId, ActionResult? error)> RequireOwnerAsync(string slug)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return (null, NotFound());
        if (board.OwnerId != UserId) return (null, Forbid());
        return (board.Id, null);
    }
}
