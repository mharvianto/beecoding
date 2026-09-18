using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[Authorize]
[Route("api/boards")]
public class BoardsController(AppDbContext db, BoardService boards, VisibilityService vis,
    IBoardNotifier notifier, AdminAccess admin, AuditLog audit, OrgResolver orgs, OrgAccess orgAccess,
    PlagiarismService plagiarism) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly VisibilityService _vis = vis;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly AdminAccess _admin = admin;
    private readonly AuditLog _audit = audit;
    private readonly OrgResolver _orgs = orgs;
    private readonly OrgAccess _orgAccess = orgAccess;
    private readonly PlagiarismService _plagiarism = plagiarism;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BoardDto>>> Mine()
    {
        var rows = await _db.BoardMemberships
            .Where(m => m.UserId == UserId)
            .Include(m => m.Board!).ThenInclude(b => b.Members)
            .Include(m => m.Board!).ThenInclude(b => b.Problems)
            .Include(m => m.Board!).ThenInclude(b => b.Organization)
            .ToListAsync();

        // Board's query filter can leave a stale membership's Board navigation null
        // (the board was deleted but the membership row wasn't cleaned up).
        return rows
            .Where(m => m.Board is not null)
            .OrderByDescending(m => m.Board!.CreatedAt)
            .Select(m => ToDto(m.Board!, m.Role))
            .ToList();
    }

    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<BoardDto>> Create(CreateBoardDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required.");

        int? organizationId;
        if (dto.OrganizationId is int requested)
        {
            var isMember = await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == requested && m.UserId == UserId);
            if (!isMember) return Forbid();
            organizationId = requested;
        }
        else
        {
            // Unambiguous only — a teacher in several organizations must pick explicitly.
            organizationId = await _orgs.ForUserAsync(UserId);
        }

        var board = new Board
        {
            Title = dto.Title.Trim(),
            OwnerId = UserId,
            OrganizationId = organizationId,
            JoinCode = await _boards.GenerateJoinCodeAsync(),
            Slug = await _boards.GenerateSlugAsync(),
        };
        _db.Boards.Add(board);
        _db.BoardMemberships.Add(new BoardMembership
        {
            Board = board,
            UserId = UserId,
            Role = MembershipRole.Owner,
        });
        await _db.SaveChangesAsync();
        if (organizationId is int orgId) board.Organization = await _db.Organizations.FindAsync(orgId);

        return ToDto(board, MembershipRole.Owner);
    }

    [HttpPost("join")]
    public async Task<ActionResult<BoardDto>> Join(JoinBoardDto dto)
    {
        var code = (dto.Code ?? "").Trim().ToUpperInvariant();
        var board = await _db.Boards
            .Include(b => b.Members)
            .Include(b => b.Problems)
            .Include(b => b.Organization)
            .FirstOrDefaultAsync(b => b.JoinCode == code);
        if (board is null) return NotFound("No board with that code.");

        var existing = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (existing is not null) return ToDto(board, existing.Role);

        var role = CurrentRole == "Teacher" ? MembershipRole.Teacher : MembershipRole.Student;
        _db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = UserId, Role = role });
        await _db.SaveChangesAsync();

        return ToDto(board, role);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<BoardDto>> Get(string slug)
    {
        var board = await _db.Boards
            .Include(b => b.Members)
            .Include(b => b.Problems)
            .Include(b => b.Organization)
            .FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();

        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (membership is null && !IsAdminUser(_admin)) return Forbid();
        return ToDto(board, membership?.Role ?? MembershipRole.Teacher);
    }

    [HttpPatch("{slug}")]
    public async Task<ActionResult<BoardDto>> Update(string slug, UpdateBoardDto dto)
    {
        var board = await _db.Boards.Include(b => b.Members).Include(b => b.Problems)
            .FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();

        bool isOwner = board.OwnerId == UserId;
        bool isPlatformAdmin = IsAdminUser(_admin);
        bool canManageOrg = board.OrganizationId is int oid && await _orgAccess.CanManageAsync(UserId, ActorEmail, oid);
        if (!isOwner && !isPlatformAdmin && !canManageOrg) return Forbid();

        // Exam mode / protect content / lecturing mode stay owner-or-platform-admin only —
        // an org admin can tag/organize a board for their institution without also being
        // able to remotely flip a teacher's in-class settings.
        if (!isOwner && !isPlatformAdmin
            && (dto.ExamMode is not null || dto.ProtectContent is not null || dto.LecturingMode is not null))
            return Forbid();

        if (dto.ExamMode is bool exam) board.ExamMode = exam;
        if (dto.ProtectContent is bool protect) board.ProtectContent = protect;
        if (dto.LecturingMode is bool lecture) board.LecturingMode = lecture;
        if (dto.Tags is not null) board.Tags = Mapping.NormalizeTags(dto.Tags);
        await _db.SaveChangesAsync();
        await _notifier.ExamModeChangedAsync(board.Id, board.ExamMode);
        await _notifier.BoardSettingsChangedAsync(board.Id);

        return ToDto(board, MembershipRole.Owner);
    }

    [HttpGet("{slug}/progress")]
    public async Task<ActionResult<ProgressBoardDto>> Progress(string slug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var progress = await _boards.BuildProgressAsync(boardId.Value, UserId, IsAdminUser(_admin));
        return progress is null ? Forbid() : progress;
    }

    /// <summary>Staff-only (Owner/Teacher) statistics for this one board — weekly activity
    /// trend and topic breakdown, scoped to this board's own problems/submissions only.
    /// Deliberately not a cross-board dashboard (see the Org Admin / platform Admin
    /// dashboards for that shape) — a teacher checks this from inside the board they're
    /// currently looking at.</summary>
    [HttpGet("{slug}/stats")]
    public async Task<ActionResult<BoardStatsDto>> Stats(string slug)
    {
        var board = await _db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        bool isAdmin = IsAdminUser(_admin);
        if (!isAdmin && (membership is null || membership.Role == MembershipRole.Student)) return Forbid();

        var totalStudents = board.Members.Count(m => m.Role == MembershipRole.Student);
        var totalProblems = await _db.Problems.CountAsync(p => p.BoardId == board.Id);
        var subs = await _db.Submissions.Where(s => s.Problem!.BoardId == board.Id)
            .Select(s => new { s.UserId, s.Verdict, s.Score, s.CreatedAt, Tags = s.Problem!.Tags })
            .ToListAsync();
        var totalSubmissions = subs.Count;
        var accepted = subs.Count(s => s.Verdict == Verdict.Accepted && s.Score >= 1.0);

        var byTag = new Dictionary<string, (int Attempts, int Accepted)>();
        static IEnumerable<string> TagsOf(string? t) =>
            (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(x => x.ToLowerInvariant()).Distinct();
        foreach (var s in subs)
        {
            foreach (var tag in TagsOf(s.Tags))
            {
                var (a, acc) = byTag.TryGetValue(tag, out var v) ? v : (0, 0);
                a++;
                if (s.Verdict == Verdict.Accepted && s.Score >= 1.0) acc++;
                byTag[tag] = (a, acc);
            }
        }
        var topics = byTag.OrderByDescending(kv => kv.Value.Attempts).Take(8)
            .Select(kv => new AdminTopicStatDto(kv.Key, kv.Value.Attempts, kv.Value.Accepted,
                kv.Value.Attempts > 0 ? kv.Value.Accepted / (double)kv.Value.Attempts : 0))
            .ToList();

        return new BoardStatsDto(totalStudents, totalProblems, totalSubmissions, accepted, topics);
    }

    /// <summary>Staff-only engagement trend for this one board, granularity-selectable
    /// (hour/day/week) — same shape as the Org Admin / platform Admin dashboards, scoped to
    /// this board's own problems/submissions only (bank/practice activity excluded, same as
    /// those dashboards, since a bank problem belongs to a user, not a board).</summary>
    [HttpGet("{slug}/stats/engagement")]
    public async Task<ActionResult<List<AdminEngagementPointDto>>> StatsEngagement(
        string slug, [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        var board = await _db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (!IsAdminUser(_admin) && (membership is null || membership.Role == MembershipRole.Student)) return Forbid();

        var g = TimeBucketing.NormalizeGranularity(granularity);
        periods = Math.Clamp(periods, 1, g == "hour" ? 168 : g == "day" ? 90 : 52);

        var activity = await _db.Submissions.Where(s => s.Problem!.BoardId == board.Id)
            .Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();

        return activity.GroupBy(x => TimeBucketing.BucketStart(x.CreatedAt, g))
            .OrderByDescending(x => x.Key).Take(periods).OrderBy(x => x.Key)
            .Select(x => new AdminEngagementPointDto(
                TimeBucketing.FormatPeriodStart(x.Key, g), x.Select(y => y.UserId).Distinct().Count(), x.Count()))
            .ToList();
    }

    /// <summary>Staff-only AI usage trend for this one board. AiUsage isn't tracked per-board
    /// (only per user/day) — proxied by summing across this board's current members, same
    /// approach as the Org Admin dashboard's AI chart. A user on several boards shows up
    /// under each.</summary>
    [HttpGet("{slug}/stats/ai-engagement")]
    public async Task<ActionResult<List<AdminAiEngagementPointDto>>> StatsAiEngagement(
        string slug, [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        var board = await _db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (!IsAdminUser(_admin) && (membership is null || membership.Role == MembershipRole.Student)) return Forbid();

        var g = TimeBucketing.NormalizeGranularity(granularity);
        if (g == "hour") g = "day";
        periods = Math.Clamp(periods, 1, g == "day" ? 90 : 52);

        var memberIds = board.Members.Select(m => m.UserId).ToList();
        var rows = await _db.AiUsages.Where(x => memberIds.Contains(x.UserId))
            .Select(x => new { x.Day, x.Calls, x.PromptTokens, x.CompletionTokens }).ToListAsync();

        DateOnly BucketDay(DateOnly d) => g == "week" ? d.AddDays(-(((int)d.DayOfWeek + 6) % 7)) : d;

        return rows.GroupBy(x => BucketDay(x.Day))
            .OrderByDescending(x => x.Key).Take(periods).OrderBy(x => x.Key)
            .Select(x => new AdminAiEngagementPointDto(
                x.Key.ToString("yyyy-MM-dd"), x.Sum(y => y.Calls), x.Sum(y => y.PromptTokens) + x.Sum(y => y.CompletionTokens)))
            .ToList();
    }

    /// <summary>Staff-only, searchable submission history for this one board — every
    /// submission on any of its problems, not just the latest-per-student shown on the
    /// progress grid. `userId` narrows to one student (the roster's "Submissions" link).</summary>
    [HttpGet("{slug}/submissions")]
    public async Task<ActionResult<AdminPageDto<AdminSubmissionRow>>> Submissions(
        string slug, [FromQuery] string? q, [FromQuery] string? verdict,
        [FromQuery] int? userId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var board = await _db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (!IsAdminUser(_admin) && (membership is null || membership.Role == MembershipRole.Student)) return Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var n = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        Verdict? v = !string.IsNullOrWhiteSpace(verdict) && Enum.TryParse<Verdict>(verdict, true, out var vv) ? vv : null;

        var query = _db.Submissions.Where(s => s.Problem!.BoardId == board.Id);
        if (userId is int uid) query = query.Where(s => s.UserId == uid);
        if (n is not null)
            query = query.Where(s => EF.Functions.Like(s.User!.Email, $"%{n}%")
                || EF.Functions.Like(s.User!.DisplayName, $"%{n}%")
                || EF.Functions.Like(s.Problem!.Title, $"%{n}%"));
        if (v is Verdict bv) query = query.Where(s => s.Verdict == bv);

        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new AdminSubmissionRow(
                s.Id, s.CreatedAt, s.Verdict.ToString(), s.Score, s.RuntimeMs, s.MemoryKb, s.Language,
                s.UserId, s.User!.Email, s.User.DisplayName, s.Problem!.Title, board.Slug, board.Title, "Board", s.Problem.Slug))
            .ToListAsync();

        return new AdminPageDto<AdminSubmissionRow>(rows, total, page, pageSize);
    }

    /// <summary>Staff-only: flag pairs of students on this board whose latest submission to
    /// the same problem looks suspiciously similar (see PlagiarismService for the algorithm
    /// and its caveats — this is a spot-check signal, not proof).</summary>
    [HttpGet("{slug}/plagiarism")]
    public async Task<ActionResult<List<PlagiarismPairDto>>> Plagiarism(string slug)
    {
        var board = await _db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (!IsAdminUser(_admin) && (membership is null || membership.Role == MembershipRole.Student)) return Forbid();

        return await _plagiarism.ComputeForBoardAsync(board.Id);
    }

    /// <summary>Soft-delete (owner or admin). Owner undo is time-boxed to
    /// <see cref="SoftDelete.UndoWindow"/>; an admin can restore any time (see the admin
    /// trash view).</summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        bool isAdmin = IsAdminUser(_admin);
        if (board.OwnerId != UserId && !isAdmin) return Forbid();

        board.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "Board", board.Id, board.Title);
        return NoContent();
    }

    [HttpPost("{slug}/restore")]
    public async Task<IActionResult> Restore(string slug)
    {
        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        bool isAdmin = IsAdminUser(_admin);
        if (board.OwnerId != UserId && !isAdmin) return Forbid();
        if (!isAdmin && !SoftDelete.CanRestore(board.DeletedAt)) return StatusCode(StatusCodes.Status410Gone, "The undo window has expired.");

        board.DeletedAt = null;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "restore", "Board", board.Id, board.Title);
        return NoContent();
    }

    private BoardDto ToDto(Board b, MembershipRole role) => new(
        b.Id, b.Slug, b.Title, b.JoinCode, b.ExamMode, b.ProtectContent, b.LecturingMode,
        b.OwnerId == UserId, role.ToString(),
        b.Members?.Count(m => m.Role == MembershipRole.Student) ?? 0,
        b.Problems?.Count ?? 0,
        b.OrganizationId, b.Organization?.Name, b.Tags);
}
