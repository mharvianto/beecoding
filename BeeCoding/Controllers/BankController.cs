using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// A teacher's private problem bank. Problems are private by default; marking one public
/// lets other teachers browse and copy it (hidden test cases are never sent to them — the
/// copy happens server-side). Adding to a board COPIES: the board's problem is independent.
/// </summary>
[ApiController]
[Authorize]
public class BankController(AppDbContext db, IBoardNotifier notifier, AdminAccess admin, AuditLog audit, NotificationService notifications, PlatformRuntimeConfig runtimeConfig) : ApiControllerBase
{
    /// <summary>A student's bank is private to them and only a staging area: nothing reaches practice or a board
    /// without a reviewer (see SubmitForPractice / ProposeToBoard). These caps keep it from being abused as storage;
    /// the problem count is admin-configurable (PlatformRuntimeConfig.StudentMaxProblems).</summary>
    private const int MaxStudentTests = 30;
    private const int MaxPendingPerUser = 10;

    private readonly AppDbContext _db = db;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly AdminAccess _admin = admin;
    private readonly AuditLog _audit = audit;
    private readonly NotificationService _notifications = notifications;
    private readonly PlatformRuntimeConfig _runtimeConfig = runtimeConfig;

    private bool IsStudent => CurrentRole == "Student";

    // Students see their own problems only — the shared bank is a teacher feature (practice already
    // lists the public ones to them).
    private IQueryable<BankProblem> Readable() => IsStudent
        ? _db.BankProblems.Where(b => b.OwnerId == UserId)
        : _db.BankProblems.Where(b => b.OwnerId == UserId || b.IsPublic);

    [HttpGet("api/bank")]
    public async Task<ActionResult<IEnumerable<BankSummaryDto>>> List(
        [FromQuery] string? q, [FromQuery] string? tag, [FromQuery] string? scope,
        [FromQuery] string? level)
    {
        var query = (scope ?? "all").ToLowerInvariant() switch
        {
            "mine" => _db.BankProblems.Where(b => b.OwnerId == UserId),
            "public" => _db.BankProblems.Where(b => !IsStudent && b.IsPublic && b.OwnerId != UserId),
            _ => Readable(),
        };

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            query = query.Where(b => EF.Functions.Like(b.Title, $"%{needle}%")
                                  || EF.Functions.Like(b.Tags, $"%{needle}%"));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim().ToLowerInvariant();
            query = query.Where(b => EF.Functions.Like(b.Tags, $"%{t}%"));
        }
        if (Enum.TryParse<ProblemLevel>(level, ignoreCase: true, out var lvl))
        {
            query = query.Where(b => b.Level == lvl);
        }

        var rows = await query
            .Include(b => b.Owner)
            .Include(b => b.TestCases)
            .OrderByDescending(b => b.UpdatedAt)
            .Take(200)
            .ToListAsync();

        return rows.Select(b => Mapping.ToSummary(b, UserId)).ToList();
    }

    [HttpGet("api/bank/{slug}")]
    public async Task<ActionResult<BankProblemDto>> Get(string slug)
    {
        var b = await Readable()
            .Include(x => x.Owner)
            .Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Slug == slug);
        return b is null ? NotFound() : Mapping.ToDto(b, UserId);
    }

    [HttpPost("api/bank")]
    public async Task<ActionResult<BankProblemDto>> Create(UpsertBankProblemDto dto)
    {
        if (IsStudent)
        {
            var max = _runtimeConfig.StudentMaxProblems;
            if (await _db.BankProblems.CountAsync(x => x.OwnerId == UserId) >= max)
                return Conflict(max == 0 ? "Students can't create problems right now." : $"You can keep up to {max} problems in your bank — delete one first.");
            if ((dto.TestCases?.Count() ?? 0) > MaxStudentTests) return BadRequest($"At most {MaxStudentTests} test cases.");
        }
        if (ProblemRules.Validate(dto.Title, dto.StatementMarkdown, dto.TestCases, testsRequired: true) is { } invalid) return BadRequest(invalid);
        var b = new BankProblem { OwnerId = UserId };
        Apply(b, dto);
        if (IsStudent) b.IsPublic = false;   // a student never publishes directly
        _db.BankProblems.Add(b);
        await _db.SaveChangesAsync();

        await _db.Entry(b).Reference(x => x.Owner).LoadAsync();
        return Mapping.ToDto(b, UserId);
    }

    [HttpPut("api/bank/{slug}")]
    public async Task<ActionResult<BankProblemDto>> Update(string slug, UpsertBankProblemDto dto)
    {
        var b = await _db.BankProblems
            .Include(x => x.Owner)
            .Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.OwnerId != UserId) return Forbid();
        if (IsStudent)
        {
            // Frozen while a reviewer may be looking at it, and once it is live in practice.
            if (b.IsPublic || b.PendingReview)
                return Conflict("This problem is under review or already published, so it can't be edited.");
            if (await _db.ProblemProposals.AnyAsync(x => x.BankProblemId == b.Id && x.Status == ProposalStatus.Pending))
                return Conflict("This problem has a pending board proposal, so it can't be edited.");
            if ((dto.TestCases?.Count() ?? 0) > MaxStudentTests) return BadRequest($"At most {MaxStudentTests} test cases.");
        }
        if (ProblemRules.Validate(dto.Title, dto.StatementMarkdown, dto.TestCases, testsRequired: false) is { } invalid) return BadRequest(invalid);
        bool wasPublic = b.IsPublic;

        // Captured before Apply() adds any brand-new TestCase rows to b.TestCases — those
        // start with Id 0 (not yet persisted) and must never reach the removal filter below,
        // or EF throws trying to mark a never-saved entity as Deleted.
        var existingIds = b.TestCases.Select(t => t.Id).ToHashSet();

        Apply(b, dto);
        if (IsStudent) b.IsPublic = wasPublic;
        if (dto.TestCases is not null)
        {
            var keep = dto.TestCases.Where(t => t.Id is > 0).Select(t => t.Id!.Value).ToHashSet();
            _db.BankTestCases.RemoveRange(b.TestCases.Where(t => existingIds.Contains(t.Id) && !keep.Contains(t.Id)));
        }
        await _db.SaveChangesAsync();

        var fresh = await _db.BankProblems.Include(x => x.Owner).Include(x => x.TestCases)
            .FirstAsync(x => x.Id == b.Id);
        return Mapping.ToDto(fresh, UserId);
    }

    /// <summary>Soft-delete (owner or admin — admin need not be a Teacher). Undo-able for
    /// <see cref="SoftDelete.UndoWindow"/>.</summary>
    [HttpDelete("api/bank/{slug}")]
    public async Task<IActionResult> Delete(string slug)
    {
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.OwnerId != UserId && !IsAdminUser(_admin)) return Forbid();

        b.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "BankProblem", b.Id, b.Title);
        return NoContent();
    }

    [HttpPost("api/bank/{slug}/restore")]
    public async Task<IActionResult> Restore(string slug)
    {
        var b = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        bool isAdmin = IsAdminUser(_admin);
        if (b.OwnerId != UserId && !isAdmin) return Forbid();
        if (!isAdmin && !SoftDelete.CanRestore(b.DeletedAt)) return StatusCode(StatusCodes.Status410Gone, "The undo window has expired.");

        b.DeletedAt = null;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "restore", "BankProblem", b.Id, b.Title);
        return NoContent();
    }

    /// <summary>Ask for the problem to be published to practice. An admin reviews it in the same queue as AI-generated
    /// problems (Admin → review); until then it stays private to its owner.</summary>
    [HttpPost("api/bank/{slug}/submit-practice")]
    public async Task<IActionResult> SubmitForPractice(string slug)
    {
        var b = await _db.BankProblems.Include(x => x.TestCases).FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.OwnerId != UserId) return Forbid();
        if (b.IsPublic) return Conflict("This problem is already public.");
        if (b.PendingReview) return Conflict("This problem is already waiting for review.");
        if (string.IsNullOrWhiteSpace(b.Title) || string.IsNullOrWhiteSpace(b.StatementMarkdown))
            return BadRequest("Add a title and a statement first.");
        if (b.TestCases.Count == 0) return BadRequest("Add at least one test case first.");
        if (b.TestCases.All(t => t.IsSample)) return BadRequest(ProblemRules.NeedsHiddenTest);
        if (await _db.BankProblems.CountAsync(x => x.OwnerId == UserId && x.PendingReview) >= MaxPendingPerUser)
            return Conflict($"You already have {MaxPendingPerUser} problems waiting for review.");

        b.PendingReview = true;
        b.ReviewNote = null;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var name = await _db.Users.Where(u => u.Id == UserId).Select(u => u.DisplayName).FirstAsync();
        await _notifications.ReviewSubmittedAsync(UserId, name, b.Title);
        return NoContent();
    }

    /// <summary>Ask the owner of a board you belong to to add this problem to it. They approve or decline from the
    /// board's Problems page; approval copies it onto the board, hidden.</summary>
    [HttpPost("api/bank/{slug}/propose/{boardSlug}")]
    public async Task<IActionResult> ProposeToBoard(string slug, string boardSlug, ProposeDto dto)
    {
        var b = await _db.BankProblems.Include(x => x.TestCases).FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.OwnerId != UserId) return Forbid();

        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Slug == boardSlug);
        if (board is null || !await _db.BoardMemberships.AnyAsync(m => m.BoardId == board.Id && m.UserId == UserId))
            return NotFound();
        if (board.OwnerId == UserId) return BadRequest("You own this board — add the problem to it directly.");
        if (!board.AllowProposals) return Conflict("The owner of this board isn't accepting proposals.");
        if (b.TestCases.Count == 0) return BadRequest("Add at least one test case first.");
        if (b.TestCases.All(t => t.IsSample)) return BadRequest(ProblemRules.NeedsHiddenTest);
        if (await _db.ProblemProposals.AnyAsync(x => x.BankProblemId == b.Id && x.BoardId == board.Id && x.Status == ProposalStatus.Pending))
            return Conflict("This problem is already proposed to that board.");
        if (await _db.ProblemProposals.CountAsync(x => x.ProposerId == UserId && x.Status == ProposalStatus.Pending) >= MaxPendingPerUser)
            return Conflict($"You already have {MaxPendingPerUser} proposals waiting for a reply.");

        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        var proposal = new ProblemProposal
        {
            BankProblemId = b.Id, BoardId = board.Id, ProposerId = UserId,
            Note = note is { Length: > 300 } ? note[..300] : note,
        };
        _db.ProblemProposals.Add(proposal);
        await _db.SaveChangesAsync();

        var name = await _db.Users.Where(u => u.Id == UserId).Select(u => u.DisplayName).FirstAsync();
        await _notifications.ProposalFiledAsync(proposal, board.OwnerId, name, b.Title, $"/boards/{board.Slug}/problems");
        return NoContent();
    }

    /// <summary>Take a practice submission back while it is still waiting for review.</summary>
    [HttpPost("api/bank/{slug}/withdraw-practice")]
    public async Task<IActionResult> WithdrawPractice(string slug)
    {
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.OwnerId != UserId) return Forbid();
        if (!b.PendingReview) return Conflict("This problem isn't waiting for review.");
        // A teacher's AI draft is queued automatically, not submitted — leave that to the admin queue.
        if (b.GeneratedByAi && !IsStudent) return Conflict("This problem was queued automatically and can't be withdrawn.");

        b.PendingReview = false;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifications.ReviewWithdrawnAsync(UserId, b.Title);
        return NoContent();
    }

    /// <summary>Take a board proposal back while its owner hasn't answered it.</summary>
    [HttpPost("api/me/proposals/{id:int}/withdraw")]
    public async Task<IActionResult> WithdrawProposal(int id)
    {
        var p = await _db.ProblemProposals.FirstOrDefaultAsync(x => x.Id == id && x.ProposerId == UserId);
        if (p is null) return NotFound();
        if (p.Status != ProposalStatus.Pending) return Conflict("This proposal was already answered.");

        p.Status = ProposalStatus.Withdrawn;
        p.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Every proposal the caller made (recent first), so the bank can show where each problem stands.</summary>
    [HttpGet("api/me/proposals")]
    public async Task<IEnumerable<MyProposalDto>> AllMyProposals() =>
        await _db.ProblemProposals.Where(x => x.ProposerId == UserId && x.BankProblem != null)
            .OrderByDescending(x => x.CreatedAt).Take(200)
            .Select(x => new MyProposalDto(x.Id, x.Board!.Slug, x.Board.Title, x.Status.ToString(), x.Note, x.ReviewNote, x.CreatedAt, x.BankProblem!.Slug))
            .ToListAsync();

    /// <summary>The caller's proposals for one of their bank problems, newest first.</summary>
    [HttpGet("api/bank/{slug}/proposals")]
    public async Task<ActionResult<IEnumerable<MyProposalDto>>> MyProposals(string slug)
    {
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.OwnerId != UserId) return Forbid();

        return await _db.ProblemProposals.Where(x => x.BankProblemId == b.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MyProposalDto(x.Id, x.Board!.Slug, x.Board.Title, x.Status.ToString(), x.Note, x.ReviewNote, x.CreatedAt, b.Slug))
            .ToListAsync();
    }

    /// <summary>Copy a bank problem (with all its tests) onto a board the caller owns.</summary>
    [Authorize(Roles = "Teacher")]
    [HttpPost("api/bank/{id:int}/copy-to/{slug}")]
    public async Task<ActionResult<ProblemDto>> CopyToBoard(int id, string slug)
    {
        var bank = await Readable().Include(x => x.TestCases).FirstOrDefaultAsync(x => x.Id == id);
        if (bank is null) return NotFound();

        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Slug == slug);
        if (board is null) return NotFound();
        if (board.OwnerId != UserId) return Forbid();

        int nextPos = await _db.Problems.Where(p => p.BoardId == board.Id)
            .Select(p => (int?)p.Position).MaxAsync() is int max ? max + 1 : 0;

        var p = BoardBulkService.ProblemFromBank(bank, board.Id, nextPos);

        _db.Problems.Add(p);
        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(board.Id);

        return Mapping.ToOwnerDto(p);
    }

    /// <summary>Save an existing board problem into the caller's bank.</summary>
    [Authorize(Roles = "Teacher")]
    [HttpPost("api/boards/{slug}/problems/{problemId:int}/to-bank")]
    public async Task<ActionResult<BankProblemDto>> SaveToBank(string slug, int problemId)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Slug == slug);
        if (board is null) return NotFound();
        if (board.OwnerId != UserId) return Forbid();

        var p = await _db.Problems.Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Id == problemId && x.BoardId == board.Id);
        if (p is null) return NotFound();

        var b = new BankProblem
        {
            OwnerId = UserId,
            Title = p.Title,
            StatementMarkdown = p.StatementMarkdown,
            AllowedLanguages = p.AllowedLanguages,
            Tags = p.Tags,
            Level = p.Level,
            GeneratedByAi = p.GeneratedByAi,
            BannedHeaders = p.BannedHeaders,
            BannedSymbols = p.BannedSymbols,
            InputFileName = p.InputFileName,
            TimeLimitMs = p.TimeLimitMs,
            MemoryLimitKb = p.MemoryLimitKb,
        };
        foreach (var t in p.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id))
            b.TestCases.Add(new BankTestCase
            {
                Stdin = t.Stdin,
                ExpectedStdout = t.ExpectedStdout,
                IsSample = t.IsSample,
                Points = t.Points,
                Position = t.Position,
            });

        _db.BankProblems.Add(b);
        await _db.SaveChangesAsync();

        await _db.Entry(b).Reference(x => x.Owner).LoadAsync();
        return Mapping.ToDto(b, UserId);
    }

    private static void Apply(BankProblem b, UpsertBankProblemDto dto)
    {
        b.Title = (dto.Title ?? "").Trim();
        b.StatementMarkdown = dto.StatementMarkdown ?? "";
        b.AllowedLanguages = Languages.Normalize(dto.AllowedLanguages);
        b.Level = Mapping.ParseLevel(dto.Level);
        b.BannedHeaders = SourcePolicy.Normalize(dto.BannedHeaders);
        b.BannedSymbols = SourcePolicy.NormalizeSymbols(dto.BannedSymbols);
        b.InputFileName = InputFilePolicy.Normalize(dto.InputFileName);
        b.TimeLimitMs = Math.Clamp(dto.TimeLimitMs <= 0 ? 1000 : dto.TimeLimitMs, 100, 10_000);
        b.MemoryLimitKb = Math.Clamp(dto.MemoryLimitKb <= 0 ? 32_768 : dto.MemoryLimitKb, 4_096, 512_000);
        b.Tags = Mapping.NormalizeTags(dto.Tags);
        b.IsPublic = dto.IsPublic;
        b.UpdatedAt = DateTime.UtcNow;

        foreach (var t in dto.TestCases ?? Enumerable.Empty<UpsertTestCaseDto>())
        {
            var tc = t.Id is > 0 ? b.TestCases.FirstOrDefault(x => x.Id == t.Id) : null;
            if (tc is null)
            {
                tc = new BankTestCase { BankProblem = b };
                b.TestCases.Add(tc);
            }
            tc.Stdin = t.Stdin ?? "";
            tc.ExpectedStdout = t.ExpectedStdout ?? "";
            tc.IsSample = t.IsSample;
            tc.Points = Math.Max(0, t.Points);
            tc.Position = t.Position;
        }
    }

}
