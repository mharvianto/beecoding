using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Likes and "this problem is wrong" reports, for practice (bank) problems and for problems on a board.
/// Anyone who can open a problem can like or report it; reports are reviewed by the problem's owner (board
/// staff for a board problem) and by platform admins, who resolve or dismiss them.
/// </summary>
[ApiController]
[Authorize]
public class ProblemFeedbackController(AppDbContext db, BoardService boards, VisibilityService vis, AdminAccess admin) : ApiControllerBase
{
    private const int MaxReportsPerDay = 10;
    private const int MinMessage = 10;
    private const int MaxMessage = 1000;

    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly VisibilityService _vis = vis;
    private readonly AdminAccess _admin = admin;

    private record Target(int? BankId, int? ProblemId);

    // ---------------------------------------------------------------- resolving what is being liked / reported

    private async Task<Target?> PracticeTargetAsync(string slug)
    {
        var id = await _db.BankProblems.Where(b => b.IsPublic && b.Slug == slug).Select(b => (int?)b.Id).FirstOrDefaultAsync();
        return id is null ? null : new Target(id, null);
    }

    /// <summary>A problem on a board, if the caller may open it (member who can see it, or staff / admin).</summary>
    private async Task<Target?> BoardTargetAsync(string boardSlug, string problemSlug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(boardSlug);
        if (boardId is null) return null;
        var me = await _boards.GetMembershipAsync(boardId.Value, UserId);
        bool isAdmin = IsAdminUser(_admin);
        if (me is null && !isAdmin) return null;
        var p = await _db.Problems.Include(x => x.Group).FirstOrDefaultAsync(x => x.Slug == problemSlug && x.BoardId == boardId.Value);
        if (p is null) return null;
        bool staff = me is not null ? _vis.IsStaff(me.Role) : isAdmin;
        if (!staff && !GroupAccess.StudentVisible(p, DateTime.UtcNow)) return null;
        return new Target(null, p.Id);
    }

    private IQueryable<ProblemLike> LikesOf(Target t) =>
        t.BankId is int b ? _db.ProblemLikes.Where(l => l.BankProblemId == b) : _db.ProblemLikes.Where(l => l.ProblemId == t.ProblemId);

    private IQueryable<ProblemReport> ReportsOf(Target t) =>
        t.BankId is int b ? _db.ProblemReports.Where(r => r.BankProblemId == b) : _db.ProblemReports.Where(r => r.ProblemId == t.ProblemId);

    private async Task<ProblemFeedbackDto> FeedbackAsync(Target t) =>
        new(await LikesOf(t).CountAsync(), await LikesOf(t).AnyAsync(l => l.UserId == UserId),
            await ReportsOf(t).AnyAsync(r => r.UserId == UserId && r.Status == ReportStatus.Open));

    private async Task<ActionResult<ProblemFeedbackDto>> SetLikeAsync(Target t, bool liked)
    {
        var mine = await LikesOf(t).FirstOrDefaultAsync(l => l.UserId == UserId);
        if (liked && mine is null) _db.ProblemLikes.Add(new ProblemLike { UserId = UserId, BankProblemId = t.BankId, ProblemId = t.ProblemId });
        else if (!liked && mine is not null) _db.ProblemLikes.Remove(mine);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* a double click raced itself into the unique index: the like exists, which is what was asked */ }
        return await FeedbackAsync(t);
    }

    private async Task<IActionResult> ReportAsync(Target t, ProblemReportCreateDto dto)
    {
        if (!Enum.TryParse<ReportCategory>(dto.Category, ignoreCase: true, out var category)) return BadRequest("Pick what is wrong.");
        var message = (dto.Message ?? "").Trim();
        if (message.Length < MinMessage) return BadRequest($"Please describe the problem in at least {MinMessage} characters.");
        if (message.Length > MaxMessage) return BadRequest($"Please keep it under {MaxMessage} characters.");

        var since = DateTime.UtcNow.AddDays(-1);
        if (await _db.ProblemReports.CountAsync(r => r.UserId == UserId && r.CreatedAt > since) >= MaxReportsPerDay)
            return StatusCode(StatusCodes.Status429TooManyRequests, "You've sent a lot of reports today. Please try again tomorrow.");
        if (await ReportsOf(t).AnyAsync(r => r.UserId == UserId && r.Status == ReportStatus.Open && r.Category == category))
            return Conflict("You already reported this. It's waiting for the author to review.");

        _db.ProblemReports.Add(new ProblemReport { UserId = UserId, BankProblemId = t.BankId, ProblemId = t.ProblemId, Category = category, Message = message });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------------------------------------------------------- practice problems

    [HttpGet("api/practice/{slug}/feedback")]
    public async Task<ActionResult<ProblemFeedbackDto>> PracticeFeedback(string slug) =>
        await PracticeTargetAsync(slug) is { } t ? await FeedbackAsync(t) : NotFound();

    [HttpPut("api/practice/{slug}/like")]
    public async Task<ActionResult<ProblemFeedbackDto>> PracticeLike(string slug) =>
        await PracticeTargetAsync(slug) is { } t ? await SetLikeAsync(t, true) : NotFound();

    [HttpDelete("api/practice/{slug}/like")]
    public async Task<ActionResult<ProblemFeedbackDto>> PracticeUnlike(string slug) =>
        await PracticeTargetAsync(slug) is { } t ? await SetLikeAsync(t, false) : NotFound();

    [HttpPost("api/practice/{slug}/report")]
    public async Task<IActionResult> PracticeReport(string slug, ProblemReportCreateDto dto) =>
        await PracticeTargetAsync(slug) is { } t ? await ReportAsync(t, dto) : NotFound();

    // ---------------------------------------------------------------- board problems

    [HttpGet("api/boards/{slug}/problems/{problemSlug}/feedback")]
    public async Task<ActionResult<ProblemFeedbackDto>> BoardFeedback(string slug, string problemSlug) =>
        await BoardTargetAsync(slug, problemSlug) is { } t ? await FeedbackAsync(t) : NotFound();

    [HttpPut("api/boards/{slug}/problems/{problemSlug}/like")]
    public async Task<ActionResult<ProblemFeedbackDto>> BoardLike(string slug, string problemSlug) =>
        await BoardTargetAsync(slug, problemSlug) is { } t ? await SetLikeAsync(t, true) : NotFound();

    [HttpDelete("api/boards/{slug}/problems/{problemSlug}/like")]
    public async Task<ActionResult<ProblemFeedbackDto>> BoardUnlike(string slug, string problemSlug) =>
        await BoardTargetAsync(slug, problemSlug) is { } t ? await SetLikeAsync(t, false) : NotFound();

    [HttpPost("api/boards/{slug}/problems/{problemSlug}/report")]
    public async Task<IActionResult> BoardReport(string slug, string problemSlug, ProblemReportCreateDto dto) =>
        await BoardTargetAsync(slug, problemSlug) is { } t ? await ReportAsync(t, dto) : NotFound();

    // ---------------------------------------------------------------- review (owners and admins)

    /// <summary>The reports the caller may review: all of them for a platform admin; for a teacher, those on
    /// their own bank problems and on boards where they are staff. Students get none.</summary>
    private IQueryable<ProblemReport> Reviewable()
    {
        if (IsAdminUser(_admin)) return _db.ProblemReports;
        if (CurrentRole != nameof(UserRole.Teacher)) return _db.ProblemReports.Where(_ => false);
        int me = UserId;
        return _db.ProblemReports.Where(r =>
            (r.BankProblemId != null && _db.BankProblems.Any(b => b.Id == r.BankProblemId && b.OwnerId == me))
            || (r.ProblemId != null && _db.Problems.Any(p => p.Id == r.ProblemId
                && p.Board!.Members.Any(m => m.UserId == me && (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Teacher)))));
    }

    [HttpGet("api/problem-reports")]
    public async Task<ActionResult<ProblemReportPageDto>> ListReports(
        [FromQuery] string? status, [FromQuery] int? bankProblemId, [FromQuery] int? problemId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var all = Reviewable();
        int openTotal = await all.CountAsync(r => r.Status == ReportStatus.Open);

        var q = all;
        if (Enum.TryParse<ReportStatus>(status, ignoreCase: true, out var st)) q = q.Where(r => r.Status == st);
        if (bankProblemId is int bid) q = q.Where(r => r.BankProblemId == bid);
        if (problemId is int pid) q = q.Where(r => r.ProblemId == pid);

        int total = await q.CountAsync();
        // open ones first (they need action), newest first within each group
        var rows = await q.OrderBy(r => r.Status == ReportStatus.Open ? 0 : 1).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.BankProblemId, r.ProblemId, r.Category, r.Message, r.Status, r.Note, r.CreatedAt, r.ResolvedAt, r.ResolvedByUserId,
                               Reporter = r.User!.DisplayName, ReporterEmail = r.User!.Email })
            .ToListAsync();

        var bankIds = rows.Where(r => r.BankProblemId != null).Select(r => r.BankProblemId!.Value).Distinct().ToList();
        var boardIds = rows.Where(r => r.ProblemId != null).Select(r => r.ProblemId!.Value).Distinct().ToList();
        var banks = await _db.BankProblems.IgnoreQueryFilters().Where(b => bankIds.Contains(b.Id)).Select(b => new { b.Id, b.Title, b.Slug }).ToDictionaryAsync(b => b.Id);
        var probs = await _db.Problems.IgnoreQueryFilters().Where(p => boardIds.Contains(p.Id)).Select(p => new { p.Id, p.Title, p.Slug, BoardSlug = p.Board!.Slug }).ToDictionaryAsync(p => p.Id);
        var reviewerIds = rows.Where(r => r.ResolvedByUserId != null).Select(r => r.ResolvedByUserId!.Value).Distinct().ToList();
        var reviewers = await _db.Users.Where(u => reviewerIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        bool isAdmin = IsAdminUser(_admin);

        var result = rows.Select(r =>
        {
            bool bank = r.BankProblemId != null;
            string title = bank ? (banks.TryGetValue(r.BankProblemId!.Value, out var b) ? b.Title : "(deleted problem)")
                                : (probs.TryGetValue(r.ProblemId!.Value, out var p) ? p.Title : "(deleted problem)");
            string? slug = bank ? banks.GetValueOrDefault(r.BankProblemId!.Value)?.Slug : probs.GetValueOrDefault(r.ProblemId!.Value)?.Slug;
            string? boardSlug = bank ? null : probs.GetValueOrDefault(r.ProblemId!.Value)?.BoardSlug;
            return new ProblemReportRowDto(r.Id, bank ? "practice" : "board", title, slug, boardSlug, r.BankProblemId, r.ProblemId,
                r.Category.ToString(), r.Message, r.Status.ToString(), r.Note, r.CreatedAt, r.ResolvedAt,
                r.Reporter, isAdmin ? r.ReporterEmail : null,
                r.ResolvedByUserId is int rid ? reviewers.GetValueOrDefault(rid) : null);
        }).ToList();
        return new ProblemReportPageDto(total, page, pageSize, openTotal, result);
    }

    [HttpPatch("api/problem-reports/{id:int}")]
    public async Task<IActionResult> ReviewReport(int id, ProblemReportReviewDto dto)
    {
        if (!Enum.TryParse<ReportStatus>(dto.Status, ignoreCase: true, out var status)) return BadRequest("Unknown status.");
        var note = (dto.Note ?? "").Trim();
        if (note.Length > 1000) return BadRequest("The note is too long.");

        var r = await Reviewable().FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();
        r.Status = status;
        r.Note = note;
        r.ResolvedAt = status == ReportStatus.Open ? null : DateTime.UtcNow;
        r.ResolvedByUserId = status == ReportStatus.Open ? null : UserId;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Open-report counts per problem, for the badges on the problem lists of Bank and Board problems.</summary>
    [HttpGet("api/problem-reports/open-counts")]
    public async Task<ActionResult<OpenReportCountsDto>> OpenCounts()
    {
        var open = await Reviewable().Where(r => r.Status == ReportStatus.Open).Select(r => new { r.BankProblemId, r.ProblemId }).ToListAsync();
        return new OpenReportCountsDto(
            open.Where(r => r.BankProblemId != null).GroupBy(r => r.BankProblemId!.Value).ToDictionary(g => g.Key, g => g.Count()),
            open.Where(r => r.ProblemId != null).GroupBy(r => r.ProblemId!.Value).ToDictionary(g => g.Key, g => g.Count()));
    }
}
