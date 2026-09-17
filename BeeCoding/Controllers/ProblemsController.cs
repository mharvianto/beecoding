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
    IBoardNotifier notifier, AdminAccess admin, AuditLog audit) : ApiControllerBase
{
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
            .Include(p => p.TestCases)
            .OrderBy(p => p.Position).ThenBy(p => p.Id)
            .ToListAsync();

        bool staff = me is not null ? _vis.IsStaff(me.Role) : isAdmin;
        return staff
            ? problems.Select(Mapping.ToOwnerDto).ToList()
            : problems.Where(p => !p.Hidden).Select(Mapping.ToStudentDto).ToList();
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
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId.Value);
        if (p is null) return NotFound();

        bool staff = me is not null ? _vis.IsStaff(me.Role) : IsAdminUser(_admin);
        if (!staff && p.Hidden) return NotFound();
        return staff ? Mapping.ToOwnerDto(p) : Mapping.ToStudentDto(p);
    }

    /// <summary>Owner-only: hide/unhide a problem from students (draft/not-ready), without
    /// touching its data or existing submissions — see Problem.Hidden.</summary>
    [HttpPatch("{problemSlug}/hidden")]
    public async Task<ActionResult<ProblemDto>> SetHidden(string slug, string problemSlug, UpdateProblemVisibilityDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var p = await _db.Problems.Include(x => x.TestCases)
            .FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId!.Value);
        if (p is null) return NotFound();

        p.Hidden = dto.Hidden;
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

        var p = new Problem { BoardId = boardId!.Value };
        Apply(p, dto);
        _db.Problems.Add(p);
        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(boardId.Value);

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

        Apply(p, dto);

        if (dto.TestCases is not null)
        {
            var keepIds = dto.TestCases.Where(t => t.Id is > 0).Select(t => t.Id!.Value).ToHashSet();
            _db.TestCases.RemoveRange(p.TestCases.Where(t => !keepIds.Contains(t.Id)));
        }

        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(boardId!.Value);

        var fresh = await _db.Problems.Include(x => x.TestCases).FirstAsync(x => x.Id == p.Id);
        return Mapping.ToOwnerDto(fresh);
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
