using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Scoped admin surface for one organization at a time — a university's own Org Admin
/// manages exactly their own members/boards/AI settings, and never sees another
/// organization's data. Deliberately smaller than the platform AdminUiController: no
/// trash/purge, no audit log, no analytics export here — those stay platform-super-admin
/// only (AdminUiController) since they touch data across every organization at once.
/// Every action checks OrgAccess.CanManageAsync itself (a super admin passes too) rather
/// than a static [Authorize(Policy=...)], since "can manage" is parameterized by which
/// org the URL names.
/// </summary>
[ApiController]
[Authorize]
[Route("api/org-admin")]
public class OrgAdminController(AppDbContext db, OrgAccess access, AuditLog audit, AiRuntimeSettings aiRuntime, AiProviderRuntime aiProviderRuntime, LtiPlatformOriginsCache ltiOrigins, BoardService boards) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly OrgAccess _access = access;
    private readonly AuditLog _audit = audit;
    private readonly AiRuntimeSettings _aiRuntime = aiRuntime;
    private readonly AiProviderRuntime _aiProviderRuntime = aiProviderRuntime;
    private readonly LtiPlatformOriginsCache _ltiOrigins = ltiOrigins;
    private readonly BoardService _boards = boards;

    /// <summary>Organizations the caller administers — for the org picker. Empty for a
    /// user who administers none (most users, including most super admins' everyday use).</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<OrganizationDto>>> Mine()
    {
        var orgs = await _access.ManagedOrgsAsync(UserId, ActorEmail);
        return orgs.Select(o => new OrganizationDto(o.Id, o.Name, o.Slug, o.CreatedAt)).ToList();
    }

    // ---- Dashboard: at-a-glance overview, scoped to this org only ------------
    [HttpGet("{orgId:int}/dashboard")]
    public async Task<ActionResult<OrgDashboardDto>> Dashboard(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();

        var members = await _db.OrganizationMemberships.Where(m => m.OrganizationId == orgId)
            .Select(m => new { m.UserId, m.Role, UserRole = m.User!.Role }).ToListAsync();
        var totalMembers = members.Count;
        var teacherCount = members.Count(m => m.UserRole == UserRole.Teacher);
        var studentCount = members.Count(m => m.UserRole == UserRole.Student);
        var adminCount = members.Count(m => m.Role == OrgRole.Admin);

        var totalBoards = await _db.Boards.CountAsync(b => b.OrganizationId == orgId);
        var totalProblems = await _db.Problems.CountAsync(p => p.Board!.OrganizationId == orgId);
        var totalSubmissions = await _db.Submissions.CountAsync(s => s.Problem!.Board!.OrganizationId == orgId);
        var acceptedSubmissions = await _db.Submissions.CountAsync(s =>
            s.Problem!.Board!.OrganizationId == orgId && s.Verdict == Verdict.Accepted && s.Score >= 1.0);

        // AI usage isn't tracked per-org directly (AiUsage is per-user) — sum it across this
        // org's members as the closest proxy. A user in several orgs shows up under each.
        var memberIds = members.Select(m => m.UserId).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var usage = await _db.AiUsages.Where(x => memberIds.Contains(x.UserId) && x.Day >= monthStart).ToListAsync();
        static AdminAiUsageBucket Sum(IEnumerable<AiUsage> xs)
        {
            int c = 0; long p = 0, k = 0;
            foreach (var x in xs) { c += x.Calls; p += x.PromptTokens; k += x.CompletionTokens; }
            return new AdminAiUsageBucket(c, p, k, p + k);
        }

        return new OrgDashboardDto(totalMembers, teacherCount, studentCount, adminCount,
            totalBoards, totalProblems, totalSubmissions, acceptedSubmissions,
            Sum(usage.Where(x => x.Day == today)), Sum(usage));
    }

    /// <summary>Active-users + submissions over time, scoped to this org's boards only
    /// (bank/practice activity isn't org-scopable — a bank problem belongs to a user, not
    /// an org), with a selectable bucket size (hour/day/week) — same TimeBucketing helper
    /// as the platform admin dashboard.</summary>
    [HttpGet("{orgId:int}/dashboard/engagement")]
    public async Task<ActionResult<List<AdminEngagementPointDto>>> DashboardEngagement(
        int orgId, [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var g = TimeBucketing.NormalizeGranularity(granularity);
        periods = Math.Clamp(periods, 1, g == "hour" ? 168 : g == "day" ? 90 : 52);

        var activity = await _db.Submissions.Where(s => s.Problem!.Board!.OrganizationId == orgId)
            .Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();

        return activity.GroupBy(x => TimeBucketing.BucketStart(x.CreatedAt, g))
            .OrderByDescending(x => x.Key).Take(periods).OrderBy(x => x.Key)
            .Select(x => new AdminEngagementPointDto(
                TimeBucketing.FormatPeriodStart(x.Key, g), x.Select(y => y.UserId).Distinct().Count(), x.Count()))
            .ToList();
    }

    /// <summary>AI calls + total tokens over time, summed across this org's members (AiUsage
    /// is per-user, not per-org — same proxy as Dashboard() above). Hourly isn't available
    /// (AiUsage only ever rolls up per calendar day), so "hour" silently falls back to
    /// "day" — same as the platform admin's AI usage chart.</summary>
    [HttpGet("{orgId:int}/dashboard/ai-engagement")]
    public async Task<ActionResult<List<AdminAiEngagementPointDto>>> DashboardAiEngagement(
        int orgId, [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var g = TimeBucketing.NormalizeGranularity(granularity);
        if (g == "hour") g = "day";
        periods = Math.Clamp(periods, 1, g == "day" ? 90 : 52);

        var memberIds = await _db.OrganizationMemberships.Where(m => m.OrganizationId == orgId)
            .Select(m => m.UserId).ToListAsync();
        var rows = await _db.AiUsages.Where(x => memberIds.Contains(x.UserId))
            .Select(x => new { x.Day, x.Calls, x.PromptTokens, x.CompletionTokens }).ToListAsync();

        DateOnly BucketDay(DateOnly d) => g == "week" ? d.AddDays(-(((int)d.DayOfWeek + 6) % 7)) : d;

        return rows.GroupBy(x => BucketDay(x.Day))
            .OrderByDescending(x => x.Key).Take(periods).OrderBy(x => x.Key)
            .Select(x => new AdminAiEngagementPointDto(
                x.Key.ToString("yyyy-MM-dd"), x.Sum(y => y.Calls), x.Sum(y => y.PromptTokens) + x.Sum(y => y.CompletionTokens)))
            .ToList();
    }

    /// <summary>Top tags by attempts, scoped to this org's board problems only.</summary>
    [HttpGet("{orgId:int}/dashboard/topics")]
    public async Task<ActionResult<List<AdminTopicStatDto>>> DashboardTopics(int orgId, [FromQuery] int take = 8)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        take = Math.Clamp(take, 1, 50);

        var problems = await _db.Problems.Where(p => p.Board!.OrganizationId == orgId)
            .Select(p => new { p.Id, p.Tags }).ToListAsync();
        var problemIds = problems.Select(p => p.Id).ToHashSet();
        var subs = await _db.Submissions.Where(s => problemIds.Contains(s.ProblemId))
            .Select(s => new { s.ProblemId, s.UserId, s.Verdict, s.Score }).ToListAsync();

        var byTag = new Dictionary<string, (int Attempts, int Accepted, HashSet<int> Solvers)>();
        static IEnumerable<string> TagsOf(string? t) =>
            (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(x => x.ToLowerInvariant()).Distinct();

        foreach (var p in problems)
        {
            var pSubs = subs.Where(s => s.ProblemId == p.Id).ToList();
            foreach (var tag in TagsOf(p.Tags))
            {
                var (attempts, accepted, solvers) = byTag.TryGetValue(tag, out var v) ? v : (0, 0, new HashSet<int>());
                attempts += pSubs.Count;
                foreach (var s in pSubs.Where(s => s.Verdict == Verdict.Accepted && s.Score >= 1.0)) { accepted++; solvers.Add(s.UserId); }
                byTag[tag] = (attempts, accepted, solvers);
            }
        }

        return byTag.OrderByDescending(kv => kv.Value.Attempts).Take(take)
            .Select(kv => new AdminTopicStatDto(kv.Key, kv.Value.Attempts, kv.Value.Accepted,
                kv.Value.Attempts > 0 ? kv.Value.Accepted / (double)kv.Value.Attempts : 0))
            .ToList();
    }

    /// <summary>Submissions across this org's own boards, plus — same "proxy via current
    /// membership" as the AI-usage dashboard, since a bank problem belongs to a user, not
    /// an org — practice submissions by this org's members. `userId` narrows to one member
    /// (the Members tab's "view submissions" link); `q` is a free-text fallback search.</summary>
    [HttpGet("{orgId:int}/submissions")]
    public async Task<ActionResult<AdminPageDto<AdminSubmissionRow>>> Submissions(
        int orgId, [FromQuery] string? q, [FromQuery] string? verdict, [FromQuery] string? source,
        [FromQuery] int? userId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var n = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        Verdict? v = !string.IsNullOrWhiteSpace(verdict) && Enum.TryParse<Verdict>(verdict, true, out var vv) ? vv : null;
        var src = source?.Trim().ToLowerInvariant();

        var rows = new List<AdminSubmissionRow>();

        if (src is null or "board")
        {
            var boardQuery = _db.Submissions.Where(s => s.Problem!.Board!.OrganizationId == orgId);
            if (userId is int uid0) boardQuery = boardQuery.Where(s => s.UserId == uid0);
            if (n is not null)
                boardQuery = boardQuery.Where(s => EF.Functions.Like(s.User!.Email, $"%{n}%")
                    || EF.Functions.Like(s.User!.DisplayName, $"%{n}%")
                    || EF.Functions.Like(s.Problem!.Title, $"%{n}%")
                    || EF.Functions.Like(s.Problem!.Board!.Title, $"%{n}%"));
            if (v is Verdict bv) boardQuery = boardQuery.Where(s => s.Verdict == bv);
            rows.AddRange(await boardQuery
                .Select(s => new AdminSubmissionRow(
                    s.Id, s.CreatedAt, s.Verdict.ToString(), s.Score, s.RuntimeMs, s.MemoryKb, s.Language,
                    s.UserId, s.User!.Email, s.User.DisplayName,
                    s.Problem!.Title, s.Problem.Board!.Slug, s.Problem.Board.Title, "Board"))
                .ToListAsync());
        }

        if (src is null or "practice")
        {
            var memberIds = _db.OrganizationMemberships.Where(m => m.OrganizationId == orgId).Select(m => m.UserId);
            var bankQuery = _db.BankSubmissions.Where(s => memberIds.Contains(s.UserId));
            if (userId is int uid1) bankQuery = bankQuery.Where(s => s.UserId == uid1);
            if (n is not null)
                bankQuery = bankQuery.Where(s => EF.Functions.Like(s.User!.Email, $"%{n}%")
                    || EF.Functions.Like(s.User!.DisplayName, $"%{n}%")
                    || EF.Functions.Like(s.BankProblem!.Title, $"%{n}%"));
            if (v is Verdict pv) bankQuery = bankQuery.Where(s => s.Verdict == pv);
            rows.AddRange(await bankQuery
                .Select(s => new AdminSubmissionRow(
                    s.Id, s.CreatedAt, s.Verdict.ToString(), s.Score, s.RuntimeMs, s.MemoryKb, s.Language,
                    s.UserId, s.User!.Email, s.User.DisplayName,
                    s.BankProblem!.Title, null, null, "Practice"))
                .ToListAsync());
        }

        var total = rows.Count;
        var pageRows = rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new AdminPageDto<AdminSubmissionRow>(pageRows, total, page, pageSize);
    }

    [HttpGet("{orgId:int}/summary")]
    public async Task<ActionResult<OrgSummaryDto>> Summary(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var memberCount = await _db.OrganizationMemberships.CountAsync(m => m.OrganizationId == orgId);
        var boardCount = await _db.Boards.CountAsync(b => b.OrganizationId == orgId);
        return new OrgSummaryDto(memberCount, boardCount);
    }

    [HttpGet("{orgId:int}/members")]
    public async Task<ActionResult<List<OrgMemberRow>>> Members(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        return await _db.OrganizationMemberships.Where(m => m.OrganizationId == orgId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new OrgMemberRow(m.UserId, m.User!.Email, m.User.DisplayName, m.Role.ToString(), m.JoinedAt))
            .ToListAsync();
    }

    /// <summary>Adds an EXISTING BeeCoding user (by email) to the organization — this isn't
    /// an email invite that creates an account; have them register (or launch via this
    /// org's LTI platform, which auto-enrolls) first.</summary>
    [HttpPost("{orgId:int}/members")]
    public async Task<ActionResult<OrgMemberRow>> AddMember(int orgId, OrgAddMemberDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        if (!Enum.TryParse<OrgRole>(dto.OrgRole, ignoreCase: true, out var role))
            return BadRequest("OrgRole must be 'Member' or 'Admin'.");

        var email = (dto.Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null);
        if (user is null) return NotFound($"No BeeCoding account for '{email}' yet — they need to register (or launch via this org's LTI platform) first.");

        var existing = await _db.OrganizationMemberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == user.Id);
        if (existing is not null) return Conflict("Already a member.");

        var membership = new OrganizationMembership { OrganizationId = orgId, UserId = user.Id, Role = role };
        _db.OrganizationMemberships.Add(membership);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-member-add", "Organization", orgId, $"{user.Email} as {role}");
        return new OrgMemberRow(user.Id, user.Email, user.DisplayName, role.ToString(), membership.JoinedAt);
    }

    [HttpPut("{orgId:int}/members/{userId:int}")]
    public async Task<IActionResult> SetMemberRole(int orgId, int userId, OrgSetMemberRoleDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        if (!Enum.TryParse<OrgRole>(dto.OrgRole, ignoreCase: true, out var role))
            return BadRequest("OrgRole must be 'Member' or 'Admin'.");

        var membership = await _db.OrganizationMemberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == userId);
        if (membership is null) return NotFound();
        if (membership.Role == OrgRole.Admin && role == OrgRole.Member && await LastAdminAsync(orgId, userId))
            return Conflict("This is the last admin of this organization — promote someone else first.");

        membership.Role = role;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-member-role", "Organization", orgId, $"user #{userId} -> {role}");
        return NoContent();
    }

    [HttpDelete("{orgId:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int orgId, int userId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var membership = await _db.OrganizationMemberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == userId);
        if (membership is null) return NotFound();
        if (membership.Role == OrgRole.Admin && await LastAdminAsync(orgId, userId))
            return Conflict("This is the last admin of this organization — promote someone else first.");

        _db.OrganizationMemberships.Remove(membership);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-member-remove", "Organization", orgId, $"user #{userId}");
        return NoContent();
    }

    private async Task<bool> LastAdminAsync(int orgId, int excludingUserId) =>
        !await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == orgId && m.Role == OrgRole.Admin && m.UserId != excludingUserId);

    /// <summary>Bulk-add EXISTING BeeCoding accounts as members via CSV — same "must already
    /// have an account" rule as the single-add endpoint, just many at once. Header row
    /// required with at least an 'email' column; optional 'role' column ('Member'/'Admin',
    /// default Member).</summary>
    [HttpPost("{orgId:int}/members/import")]
    public async Task<ActionResult<OrgMemberImportResult>> ImportMembers(int orgId, OrgMemberImportDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();

        var rows = CsvParser.Parse(dto.Csv ?? "");
        if (rows.Count < 2) return BadRequest("The CSV needs a header row and at least one data row.");

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int emailCol = header.IndexOf("email");
        int roleCol = header.IndexOf("role");
        if (emailCol < 0) return BadRequest("The CSV header must include an 'email' column.");

        var resultRows = new List<OrgMemberImportRow>();
        int added = 0, skipped = 0, errors = 0;

        for (int r = 1; r < rows.Count; r++)
        {
            string Get(int col) => col >= 0 && col < rows[r].Length ? rows[r][col].Trim() : "";
            var email = Get(emailCol).ToLowerInvariant();
            var roleStr = Get(roleCol);
            var role = roleStr.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? OrgRole.Admin : OrgRole.Member;

            if (email.Length == 0) continue;   // blank line
            if (!email.Contains('@'))
            { resultRows.Add(new(email, role.ToString(), false, "Invalid email")); errors++; continue; }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null);
            if (user is null)
            { resultRows.Add(new(email, role.ToString(), false, "No account yet — they need to register first")); errors++; continue; }

            var existing = await _db.OrganizationMemberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == user.Id);
            if (existing is not null)
            { resultRows.Add(new(email, role.ToString(), false, "Already a member")); skipped++; continue; }

            _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = orgId, UserId = user.Id, Role = role });
            await _db.SaveChangesAsync();
            resultRows.Add(new(email, role.ToString(), true, null));
            added++;
        }

        await _audit.RecordAsync(UserId, ActorEmail, "org-member-bulk-add", "Organization", orgId, $"{added} added, {skipped} skipped, {errors} errors");
        return new OrgMemberImportResult(added, skipped, errors, resultRows);
    }

    [HttpGet("{orgId:int}/boards")]
    public async Task<ActionResult<List<OrgBoardRow>>> Boards(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        return await _db.Boards.Where(b => b.OrganizationId == orgId)
            .Include(b => b.Owner).Include(b => b.Members).Include(b => b.Problems)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new OrgBoardRow(b.Id, b.Slug, b.Title, b.Owner != null ? b.Owner.Email : "?",
                b.Members.Count(m => m.Role == MembershipRole.Student), b.Problems.Count, b.CreatedAt))
            .ToListAsync();
    }

    /// <summary>Bulk-create boards, all owned by one existing Teacher and all assigned to
    /// this org in one go — e.g. 13 session boards for one course. The owner must already
    /// have a Teacher account (this doesn't create one) and does not need to already be an
    /// org member — creating the first board here auto-enrolls them, same as an LTI launch
    /// would.</summary>
    [HttpPost("{orgId:int}/boards/bulk")]
    public async Task<ActionResult<OrgBulkCreateBoardsResult>> BulkCreateBoards(int orgId, OrgBulkCreateBoardsDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();

        var email = (dto.OwnerEmail ?? "").Trim().ToLowerInvariant();
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null);
        if (owner is null) return NotFound($"No BeeCoding account for '{email}'.");
        if (owner.Role != UserRole.Teacher) return BadRequest($"'{email}' is not a Teacher account.");

        if (!await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == orgId && m.UserId == owner.Id))
            _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = orgId, UserId = owner.Id, Role = OrgRole.Member });

        var resultRows = new List<OrgBulkCreateBoardsRow>();
        int created = 0, errors = 0;

        foreach (var raw in dto.Titles ?? new())
        {
            var title = (raw ?? "").Trim();
            if (title.Length == 0) continue;
            if (title.Length > 200)
            { resultRows.Add(new(title, false, null, "Title too long")); errors++; continue; }

            try
            {
                var board = new Board
                {
                    Title = title,
                    OwnerId = owner.Id,
                    OrganizationId = orgId,
                    JoinCode = await _boards.GenerateJoinCodeAsync(),
                    Slug = await _boards.GenerateSlugAsync(),
                };
                _db.Boards.Add(board);
                _db.BoardMemberships.Add(new BoardMembership { Board = board, UserId = owner.Id, Role = MembershipRole.Owner });
                await _db.SaveChangesAsync();
                resultRows.Add(new(title, true, board.Slug, null));
                created++;
            }
            catch (Exception ex)
            {
                resultRows.Add(new(title, false, null, ex.Message));
                errors++;
            }
        }

        await _audit.RecordAsync(UserId, ActorEmail, "org-boards-bulk-create", "Organization", orgId, $"{created} board(s) for {owner.Email}");
        return new OrgBulkCreateBoardsResult(created, errors, resultRows);
    }

    [HttpGet("{orgId:int}/ai-settings")]
    public async Task<ActionResult<OrgAiSettingsDto>> GetAiSettings(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var row = await _db.AiSettings.FirstOrDefaultAsync(x => x.OrganizationId == orgId);
        return row is null
            ? new OrgAiSettingsDto(false, null, _aiRuntime.DailyQuotaStudent, _aiRuntime.DailyQuotaTeacher)
            : new OrgAiSettingsDto(row.Paused, row.PausedReason, row.DailyQuotaStudent, row.DailyQuotaTeacher);
    }

    /// <summary>An org's pause only stops that org's own AI usage — see AiRuntimeSettings —
    /// it can never override the platform-wide kill switch.</summary>
    [HttpPut("{orgId:int}/ai-settings")]
    public async Task<ActionResult<OrgAiSettingsDto>> SetAiSettings(int orgId, OrgAiSettingsDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var row = await _db.AiSettings.FirstOrDefaultAsync(x => x.OrganizationId == orgId);
        if (row is null)
        {
            row = new BeeCoding.Models.AiSettings { OrganizationId = orgId };
            _db.AiSettings.Add(row);
        }
        row.Paused = dto.Paused;
        row.PausedReason = dto.PausedReason;
        row.DailyQuotaStudent = Math.Max(0, dto.DailyQuotaStudent);
        row.DailyQuotaTeacher = Math.Max(0, dto.DailyQuotaTeacher);
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _aiRuntime.SetOrg(orgId, row.Paused, row.PausedReason, row.DailyQuotaStudent, row.DailyQuotaTeacher);
        await _audit.RecordAsync(UserId, ActorEmail, "org-ai-settings", "Organization", orgId,
            dto.Paused ? $"paused: {dto.PausedReason}" : $"quota {dto.DailyQuotaStudent}/{dto.DailyQuotaTeacher}");
        return new OrgAiSettingsDto(row.Paused, row.PausedReason, row.DailyQuotaStudent, row.DailyQuotaTeacher);
    }

    // ---- AI provider/credential: this org's own override -----------------------
    [HttpGet("{orgId:int}/ai-provider")]
    public async Task<ActionResult<AiProviderConfigDto>> GetAiProvider(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var row = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == orgId);
        return new AiProviderConfigDto(!string.IsNullOrEmpty(row?.ApiKey), ApiKeyPreview(row?.ApiKey), row?.BaseUrl, row?.Model, row?.GenerateModel);
    }

    /// <summary>Bring-your-own AI credential for this organization — falls back to the
    /// platform default (and from there to appsettings.json) field-by-field when left blank.
    /// Leave ApiKey blank to keep whatever key is already saved; use DELETE
    /// ai-provider/api-key to actually clear it.</summary>
    [HttpPut("{orgId:int}/ai-provider")]
    public async Task<ActionResult<AiProviderConfigDto>> SetAiProvider(int orgId, AiSetProviderConfigDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var row = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == orgId);
        if (row is null) { row = new AiProviderConfig { OrganizationId = orgId }; _db.AiProviderConfigs.Add(row); }
        if (!string.IsNullOrWhiteSpace(dto.ApiKey)) row.ApiKey = dto.ApiKey.Trim();
        row.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim();
        row.Model = string.IsNullOrWhiteSpace(dto.Model) ? null : dto.Model.Trim();
        row.GenerateModel = string.IsNullOrWhiteSpace(dto.GenerateModel) ? null : dto.GenerateModel.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _aiProviderRuntime.SetOrg(orgId, row.ApiKey, row.BaseUrl, row.Model, row.GenerateModel);
        await _audit.RecordAsync(UserId, ActorEmail, "org-ai-provider-set", "Organization", orgId, "AI provider override");
        return new AiProviderConfigDto(!string.IsNullOrEmpty(row.ApiKey), ApiKeyPreview(row.ApiKey), row.BaseUrl, row.Model, row.GenerateModel);
    }

    [HttpDelete("{orgId:int}/ai-provider/api-key")]
    public async Task<IActionResult> ClearAiProviderKey(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var row = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == orgId);
        if (row is null || row.ApiKey is null) return NoContent();
        row.ApiKey = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _aiProviderRuntime.SetOrg(orgId, null, row.BaseUrl, row.Model, row.GenerateModel);
        await _audit.RecordAsync(UserId, ActorEmail, "org-ai-provider-clear-key", "Organization", orgId, "AI provider override");
        return NoContent();
    }

    private static string? ApiKeyPreview(string? key) =>
        string.IsNullOrEmpty(key) ? null : $"••••{key[Math.Max(0, key.Length - 4)..]}";

    // ---- LTI 1.3 platform registry: this org's own platforms only --------------
    // Mirrors AdminUiController's lti-platforms endpoints, but every read/write is pinned
    // to `orgId` so an org admin can only ever see/touch platforms belonging to their own
    // organization — never another org's, and never the platform-wide unaffiliated ones.
    [HttpGet("{orgId:int}/lti-platforms/tool-config")]
    public async Task<ActionResult<AdminLtiToolConfigDto>> LtiToolConfig(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        string Abs(string path) => $"{Request.Scheme}://{Request.Host}{Url.Content("~" + path)}";
        return new AdminLtiToolConfigDto(
            LoginInitiationUrl: Abs("/lti/login"),
            LaunchUrl: Abs("/lti/launch"),
            JwksUrl: Abs("/lti/jwks"),
            DeepLinkingUrl: Abs("/lti/launch"));
    }

    [HttpGet("{orgId:int}/lti-platforms")]
    public async Task<ActionResult<List<AdminLtiPlatformDto>>> LtiPlatforms(int orgId)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var orgName = await _db.Organizations.Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync();
        return await _db.LtiPlatforms.Where(p => p.OrganizationId == orgId).OrderBy(p => p.Name)
            .Select(p => new AdminLtiPlatformDto(
                p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt,
                p.OrganizationId, orgName))
            .ToListAsync();
    }

    [HttpPost("{orgId:int}/lti-platforms")]
    public async Task<ActionResult<AdminLtiPlatformDto>> CreateLtiPlatform(int orgId, AdminUpsertLtiPlatformDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Issuer) || string.IsNullOrWhiteSpace(dto.ClientId))
            return BadRequest("Issuer and Client ID are required.");
        var org = await _db.Organizations.FindAsync(orgId);
        if (org is null) return NotFound();

        var p = new LtiPlatform
        {
            Name = dto.Name.Trim(), Issuer = dto.Issuer.Trim(), ClientId = dto.ClientId.Trim(),
            DeploymentIds = dto.DeploymentIds.Trim(), AuthLoginUrl = dto.AuthLoginUrl.Trim(),
            AuthTokenUrl = dto.AuthTokenUrl.Trim(), JwksUrl = dto.JwksUrl.Trim(), Enabled = dto.Enabled,
            OrganizationId = orgId,   // pinned — the dto's own OrganizationId (if any) is ignored
        };
        _db.LtiPlatforms.Add(p);
        await _db.SaveChangesAsync();
        await RefreshLtiOriginsAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-lti-platform-create", "LtiPlatform", p.Id, $"{p.Name} (org {org.Name})");
        return new AdminLtiPlatformDto(p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt, p.OrganizationId, org.Name);
    }

    [HttpPut("{orgId:int}/lti-platforms/{id:int}")]
    public async Task<ActionResult<AdminLtiPlatformDto>> UpdateLtiPlatform(int orgId, int id, AdminUpsertLtiPlatformDto dto)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Issuer) || string.IsNullOrWhiteSpace(dto.ClientId))
            return BadRequest("Issuer and Client ID are required.");
        var p = await _db.LtiPlatforms.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);
        if (p is null) return NotFound();   // also hides platforms owned by other orgs

        p.Name = dto.Name.Trim(); p.Issuer = dto.Issuer.Trim(); p.ClientId = dto.ClientId.Trim();
        p.DeploymentIds = dto.DeploymentIds.Trim(); p.AuthLoginUrl = dto.AuthLoginUrl.Trim();
        p.AuthTokenUrl = dto.AuthTokenUrl.Trim(); p.JwksUrl = dto.JwksUrl.Trim(); p.Enabled = dto.Enabled;
        // OrganizationId is deliberately NOT taken from dto — an org admin can never move a
        // platform to a different organization, only the platform super admin can.
        await _db.SaveChangesAsync();
        await RefreshLtiOriginsAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-lti-platform-update", "LtiPlatform", p.Id, p.Name);
        var orgName = await _db.Organizations.Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync();
        return new AdminLtiPlatformDto(p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt, p.OrganizationId, orgName);
    }

    [HttpDelete("{orgId:int}/lti-platforms/{id:int}")]
    public async Task<IActionResult> DeleteLtiPlatform(int orgId, int id)
    {
        if (!await _access.CanManageAsync(UserId, ActorEmail, orgId)) return Forbid();
        var p = await _db.LtiPlatforms.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);
        if (p is null) return NotFound();
        _db.LtiPlatforms.Remove(p);
        await _db.SaveChangesAsync();
        await RefreshLtiOriginsAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-lti-platform-delete", "LtiPlatform", id, p.Name);
        return NoContent();
    }

    /// <summary>Re-reads every enabled LTI platform's issuer into LtiPlatformOriginsCache —
    /// call after any write, so the CSP's frame-ancestors reflects the change immediately
    /// instead of only after a restart.</summary>
    private async Task RefreshLtiOriginsAsync() =>
        _ltiOrigins.Set(await _db.LtiPlatforms.Where(p => p.Enabled).Select(p => p.Issuer).ToListAsync());
}
