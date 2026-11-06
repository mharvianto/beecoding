using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lsp;
using BeeCoding.Services.Realtime;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BeeCoding.Controllers;

/// <summary>
/// The admin panel. Cookie-authed; the "Admin" role is granted to emails in
/// <c>Admin:Emails</c> (see AuthController). Separate from the token-authed
/// <see cref="AdminController"/> which is for scripting.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui")]
public class AdminUiController(
    AppDbContext db, AdminAccess admin, AuditLog audit, PasswordService pw, AiRuntimeSettings aiRuntime,
    AiProviderRuntime aiProviderRuntime, LtiPlatformOriginsCache ltiOrigins, PlatformRuntimeConfig runtimeConfig,
    NativeToolchain toolchain, IJudgeQueue judgeQueue, IOptions<JudgeOptions> judgeOpt,
    IOptions<LspOptions> lspOpt, IOptions<RealtimeStoreOptions> realtimeOpt, SysstatService sysstat,
    PlagiarismService plagiarism, ProgressService progress, BoardBulkService bulk, PasswordResetService resets, MfaService mfa,
    NotificationService notifications, SharedClangd sharedLsp, EmailService email)
    : ApiControllerBase
{
    private readonly SharedClangd _sharedLsp = sharedLsp;
    private readonly EmailService _email = email;
    private readonly BoardBulkService _bulk = bulk;
    private readonly NotificationService _notifications = notifications;
    private readonly PasswordResetService _resets = resets;
    private readonly MfaService _mfa = mfa;
    private readonly SysstatService _sysstat = sysstat;
    private readonly PlagiarismService _plagiarism = plagiarism;
    private readonly ProgressService _progress = progress;
    private readonly AppDbContext _db = db;
    private readonly AdminAccess _admin = admin;
    private readonly AuditLog _audit = audit;
    private readonly PasswordService _pw = pw;
    private readonly AiRuntimeSettings _aiRuntime = aiRuntime;
    private readonly AiProviderRuntime _aiProviderRuntime = aiProviderRuntime;
    private readonly LtiPlatformOriginsCache _ltiOrigins = ltiOrigins;
    private readonly PlatformRuntimeConfig _runtimeConfig = runtimeConfig;
    private readonly NativeToolchain _toolchain = toolchain;
    private readonly IJudgeQueue _judgeQueue = judgeQueue;
    private readonly JudgeOptions _judgeOpt = judgeOpt.Value;
    private readonly LspOptions _lspOpt = lspOpt.Value;
    private readonly RealtimeStoreOptions _realtimeOpt = realtimeOpt.Value;

    // ---- dashboard: at-a-glance overview, the default landing tab -----------
    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardDto>> Dashboard()
    {
        // In-memory: adminCount needs AdminAccess.IsAdminEmail (DB flag + Admin:Emails
        // config union), which doesn't translate to SQL.
        var activeUsers = await _db.Users.Where(u => u.DeletedAt == null)
            .Select(u => new { u.Email, u.Role }).ToListAsync();
        var totalUsers = activeUsers.Count;
        var teacherCount = activeUsers.Count(u => u.Role == UserRole.Teacher);
        var studentCount = activeUsers.Count(u => u.Role == UserRole.Student);
        var adminCount = activeUsers.Count(u => _admin.IsAdminEmail(u.Email));

        var totalBoards = await _db.Boards.CountAsync();
        var totalProblems = await _db.Problems.CountAsync();
        var totalBankProblems = await _db.BankProblems.CountAsync();

        var totalSubmissions = await _db.Submissions.CountAsync() + await _db.BankSubmissions.CountAsync();
        var acceptedSubmissions = await _db.Submissions.CountAsync(s => s.Verdict == Verdict.Accepted)
            + await _db.BankSubmissions.CountAsync(s => s.Verdict == Verdict.Accepted);

        var pendingAiReview = await _db.BankProblems.CountAsync(b => b.PendingReview);

        var trashCount = await _db.Users.CountAsync(u => u.DeletedAt != null)
            + await _db.Boards.IgnoreQueryFilters().CountAsync(b => b.DeletedAt != null)
            + await _db.Problems.IgnoreQueryFilters().CountAsync(p => p.DeletedAt != null)
            + await _db.BankProblems.IgnoreQueryFilters().CountAsync(b => b.DeletedAt != null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var usage = await _db.AiUsages.Where(x => x.Day >= monthStart).ToListAsync();
        static AdminAiUsageBucket Sum(IEnumerable<AiUsage> xs)
        {
            int c = 0; long p = 0, k = 0;
            foreach (var x in xs) { c += x.Calls; p += x.PromptTokens; k += x.CompletionTokens; }
            return new AdminAiUsageBucket(c, p, k, p + k);
        }
        var aiToday = Sum(usage.Where(x => x.Day == today));
        var aiMonth = Sum(usage);

        var recentActivity = await _db.AuditLogEntries.OrderByDescending(x => x.CreatedAt).Take(8)
            .Select(x => new AdminAuditLogRow(x.Id, x.CreatedAt, x.ActorEmail, x.Action, x.TargetType, x.TargetId, x.TargetLabel))
            .ToListAsync();

        return new AdminDashboardDto(
            totalUsers, teacherCount, studentCount, adminCount,
            totalBoards, totalProblems, totalBankProblems,
            totalSubmissions, acceptedSubmissions,
            pendingAiReview, trashCount,
            aiToday, aiMonth, recentActivity);
    }

    /// <summary>Active-users + submissions over time, for the dashboard's trend chart, with
    /// a selectable bucket size (hour/day/week) — board and bank submissions carry a
    /// precise timestamp, so this can go down to the hour. Same underlying aggregation as
    /// the Reports tab's weekly-engagement export at granularity=week.</summary>
    [HttpGet("dashboard/engagement")]
    public async Task<ActionResult<List<AdminEngagementPointDto>>> DashboardEngagement(
        [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        var g = TimeBucketing.NormalizeGranularity(granularity);
        periods = Math.Clamp(periods, 1, g == "hour" ? 168 : g == "day" ? 90 : 52);

        var from = TimeBucketing.Window(g, periods)[0];   // only the charted window, not the whole history
        var boardActivity = await _db.Submissions.Where(s => s.CreatedAt >= from).Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();
        var bankActivity = await _db.BankSubmissions.Where(s => s.CreatedAt >= from).Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();
        var all = boardActivity.Select(x => (x.UserId, x.CreatedAt)).Concat(bankActivity.Select(x => (x.UserId, x.CreatedAt)));

        var byBucket = all.ToLookup(x => TimeBucketing.BucketStart(x.CreatedAt, g));
        return TimeBucketing.Window(g, periods).Select(k => new AdminEngagementPointDto(
            TimeBucketing.FormatPeriodStart(k, g), byBucket[k].Select(y => y.UserId).Distinct().Count(), byBucket[k].Count())).ToList();
    }

    /// <summary>AI calls + total tokens over time, with a selectable bucket size — unlike
    /// <see cref="DashboardEngagement"/>, hourly isn't available here: AiUsage is only ever
    /// rolled up per calendar day (see AiUsageService.RecordAsync), so "hour" silently
    /// falls back to "day".</summary>
    [HttpGet("dashboard/ai-engagement")]
    public async Task<ActionResult<List<AdminAiEngagementPointDto>>> DashboardAiEngagement(
        [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        var g = TimeBucketing.NormalizeGranularity(granularity);
        if (g == "hour") g = "day";
        periods = Math.Clamp(periods, 1, g == "day" ? 90 : 52);

        var rows = await _db.AiUsages.Select(x => new { x.Day, x.Calls, x.PromptTokens, x.CompletionTokens }).ToListAsync();

        DateOnly BucketDay(DateOnly d) => g == "week" ? d.AddDays(-(((int)d.DayOfWeek + 6) % 7)) : d;

        var byBucket = rows.ToLookup(x => BucketDay(x.Day));
        return TimeBucketing.DayWindow(g, periods).Select(d => new AdminAiEngagementPointDto(
            d.ToString("yyyy-MM-dd"), byBucket[d].Sum(y => y.Calls), byBucket[d].Sum(y => y.PromptTokens + y.CompletionTokens))).ToList();
    }

    // ---- users -------------------------------------------------------------
    [HttpGet("users")]
    public async Task<ActionResult<AdminUserPageDto>> Users(
        [FromQuery] string? q, [FromQuery] string? filter, [FromQuery] string? sort, [FromQuery] string? dir,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var active = _db.Users.Where(u => u.DeletedAt == null);
        var stats = new AdminUserSecurityStats(
            await active.CountAsync(),
            await active.CountAsync(u => u.EmailVerifiedAt != null),
            await active.CountAsync(u => u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null),
            await active.CountAsync(u => _db.UserPasskeys.Any(k => k.UserId == u.Id)));

        var query = active;
        query = filter switch
        {
            "verified" => query.Where(u => u.EmailVerifiedAt != null),
            "unverified" => query.Where(u => u.EmailVerifiedAt == null),
            "mfa" => query.Where(u => u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null),
            "no-mfa" => query.Where(u => u.TotpEnabledAt == null && u.EmailMfaEnabledAt == null),
            "passkey" => query.Where(u => _db.UserPasskeys.Any(k => k.UserId == u.Id)),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.Trim();
            query = query.Where(u => EF.Functions.Like(u.Email, $"%{n}%") || EF.Functions.Like(u.DisplayName, $"%{n}%"));
        }

        var total = await query.CountAsync();
        // Whitelisted sort columns (the table headers); Id is always the tiebreaker so pages stay stable.
        bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<User> Order<TKey>(System.Linq.Expressions.Expression<Func<User, TKey>> key) =>
            desc ? query.OrderByDescending(key).ThenBy(u => u.Id) : query.OrderBy(key).ThenBy(u => u.Id);
        var ordered = sort switch
        {
            "name" => Order(u => u.DisplayName.ToLower()),
            "role" => Order(u => u.Role),
            "xp" => Order(u => u.Xp),
            "boards" => Order(u => _db.Boards.Count(b => b.OwnerId == u.Id)),
            "subs" => Order(u => _db.Submissions.Count(s => s.UserId == u.Id)),
            "joined" => Order(u => u.CreatedAt),
            _ => Order(u => u.Id),
        };
        var users = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var ids = users.Select(u => u.Id).ToList();
        var ownedByUser = await _db.Boards.Where(b => ids.Contains(b.OwnerId)).GroupBy(b => b.OwnerId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var subsByUser = await _db.Submissions.Where(s => ids.Contains(s.UserId)).GroupBy(s => s.UserId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);

        var passkeyUsers = (await _db.UserPasskeys.Where(k => ids.Contains(k.UserId)).Select(k => k.UserId).Distinct().ToListAsync()).ToHashSet();

        var rows = users.Select(u => new AdminUserRow(
            u.Id, u.Email, u.DisplayName, u.Role.ToString(), _admin.IsAdminEmail(u.Email),
            u.Xp, u.CreatedAt,
            ownedByUser.GetValueOrDefault(u.Id), subsByUser.GetValueOrDefault(u.Id), u.EmailVerifiedAt != null,
            u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null, passkeyUsers.Contains(u.Id))).ToList();

        return new AdminUserPageDto(rows, total, page, pageSize, stats);
    }

    /// <summary>Soft-delete a user (blocks login immediately; an active session is signed
    /// out on its next request — see Program.cs OnValidatePrincipal). Only admins ever
    /// delete/restore a user, so — unlike Board/Problem/BankProblem — there's no time-boxed
    /// "undo" here: restore works any time (also used by the trash view).</summary>
    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (id == UserId) return BadRequest("You can't delete your own account here.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "User", user.Id, user.Email);
        return NoContent();
    }

    /// <summary>Bulk version of <see cref="DeleteUser"/> — pick ids from the Users tab.
    /// Same soft-delete, same restore path (individually, or via Trash).</summary>
    [HttpPost("users/bulk-delete")]
    public async Task<ActionResult<AdminBulkDeleteUsersResult>> BulkDeleteUsers(AdminBulkDeleteUsersDto dto)
    {
        int deleted = 0;
        var errors = new List<string>();
        foreach (var id in (dto.Ids ?? new()).Distinct())
        {
            if (id == UserId) { errors.Add($"{id}: can't delete your own account"); continue; }
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) { errors.Add($"{id}: not found"); continue; }
            if (user.DeletedAt is not null) continue;

            user.DeletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.RecordAsync(UserId, ActorEmail, "delete", "User", user.Id, $"{user.Email} (bulk)");
            deleted++;
        }
        return new AdminBulkDeleteUsersResult(deleted, errors);
    }

    /// <summary>Rebuilds CurrentStreak/LongestStreak/MaxSolvedInADay from raw submission
    /// history — for one user (?userId=) or everyone. See ProgressService.RecalculateOneAsync
    /// for why these can drift from the incrementally-maintained values.</summary>
    [HttpPost("users/recalculate-progress")]
    public async Task<ActionResult<object>> RecalculateProgress([FromQuery] int? userId)
    {
        if (userId is int id)
        {
            if (!await _progress.RecalculateOneAsync(id))
                return NotFound();
            await _audit.RecordAsync(UserId, ActorEmail, "recalculate-progress", "User", id, "1 user");
            return new { updated = 1 };
        }
        int updated = await _progress.RecalculateAllAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "recalculate-progress", "User", 0, $"{updated} users (all)");
        return new { updated };
    }

    [HttpPost("users/{id:int}/restore")]
    public async Task<IActionResult> RestoreUser(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.DeletedAt = null;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "restore", "User", user.Id, user.Email);
        return NoContent();
    }

    /// <summary>Change a user's Teacher/Student role. Takes effect immediately even on an
    /// already-open session (see Program.cs OnValidatePrincipal, which refreshes the Role
    /// claim in place).</summary>
    [HttpPatch("users/{id:int}/role")]
    public async Task<IActionResult> ChangeRole(int id, AdminChangeRoleDto dto)
    {
        if (id == UserId) return BadRequest("You can't change your own role here.");
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var role))
            return BadRequest("Role must be 'Teacher' or 'Student'.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.Role == role) return NoContent();

        var from = user.Role;
        user.Role = role;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "role-change", "User", user.Id, $"{user.Email}: {from} -> {role}");
        return NoContent();
    }

    /// <summary>Grant the admin panel to a user. Stored on the user row (not config) — see
    /// AdminAccess. Takes effect on their very next request, no re-login needed.</summary>
    [HttpPost("users/{id:int}/admin")]
    public async Task<IActionResult> GrantAdmin(int id)
    {
        if (id == UserId) return BadRequest("You already have admin access.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.IsAdmin) return NoContent();

        user.IsAdmin = true;
        await _db.SaveChangesAsync();
        _admin.SetDbAdmin(user.Email, true);
        await _audit.RecordAsync(UserId, ActorEmail, "admin-grant", "User", user.Id, user.Email);
        return NoContent();
    }

    [HttpDelete("users/{id:int}/admin")]
    public async Task<IActionResult> RevokeAdmin(int id)
    {
        if (id == UserId) return BadRequest("You can't revoke your own admin access here.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (_admin.IsConfiguredAdmin(user.Email))
            return BadRequest("This admin is granted via the Admin:Emails server config — change it there.");
        if (!user.IsAdmin) return NoContent();

        user.IsAdmin = false;
        await _db.SaveChangesAsync();
        _admin.SetDbAdmin(user.Email, false);
        await _audit.RecordAsync(UserId, ActorEmail, "admin-revoke", "User", user.Id, user.Email);
        return NoContent();
    }

    /// <summary>Bulk-create accounts from a CSV roster (header row required: at least
    /// "email" and "name"/"displayname"; optional "role" and "password" columns). An
    /// existing email is left alone (just optionally added to the board); a missing
    /// password is auto-generated and returned once so it can be handed out.</summary>
    [HttpPost("users/import")]
    public async Task<ActionResult<AdminUserImportResult>> ImportUsers(AdminUserImportRequest dto)
    {
        var rows = CsvParser.Parse(dto.Csv ?? "");
        if (rows.Count < 2) return BadRequest("The CSV needs a header row and at least one data row.");

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int emailCol = header.IndexOf("email");
        int nameCol = header.IndexOf("name");
        if (nameCol < 0) nameCol = header.IndexOf("displayname");
        int roleCol = header.IndexOf("role");
        int pwCol = header.IndexOf("password");
        if (emailCol < 0 || nameCol < 0)
            return BadRequest("The CSV header must include at least 'email' and 'name' (or 'displayname') columns.");

        Board? board = null;
        if (!string.IsNullOrWhiteSpace(dto.BoardSlug))
        {
            board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == dto.BoardSlug);
            if (board is null) return BadRequest("Board not found.");
        }
        bool defaultTeacher = dto.DefaultRole?.Equals("Teacher", StringComparison.OrdinalIgnoreCase) == true;

        var resultRows = new List<AdminUserImportRow>();
        int created = 0, existing = 0, errors = 0;

        for (int r = 1; r < rows.Count; r++)
        {
            string Get(int col) => col >= 0 && col < rows[r].Length ? rows[r][col].Trim() : "";
            var email = Get(emailCol).ToLowerInvariant();
            var name = DisplayNamePolicy.Clean(Get(nameCol));
            var roleStr = Get(roleCol);
            var explicitPw = Get(pwCol);
            var role = roleStr.Equals("Teacher", StringComparison.OrdinalIgnoreCase) ? UserRole.Teacher
                : roleStr.Equals("Student", StringComparison.OrdinalIgnoreCase) ? UserRole.Student
                : defaultTeacher ? UserRole.Teacher : UserRole.Student;

            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(name)) continue;   // blank line
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            { resultRows.Add(new(email, name, role.ToString(), false, false, null, "Invalid email")); errors++; continue; }
            if (string.IsNullOrWhiteSpace(name))
            { resultRows.Add(new(email, name, role.ToString(), false, false, null, "Name is required")); errors++; continue; }
            if (DisplayNamePolicy.Validate(name, out _) is string nameErr)
            { resultRows.Add(new(email, name, role.ToString(), false, false, null, nameErr)); errors++; continue; }

            try
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
                bool wasCreated = false;
                string? generatedPw = null;
                if (user is null)
                {
                    user = new User { Email = email, DisplayName = name, Role = role, EmailVerifiedAt = DateTime.UtcNow };   // the admin vouches for imported addresses
                    var password = string.IsNullOrWhiteSpace(explicitPw) ? GeneratePassword() : explicitPw;
                    if (string.IsNullOrWhiteSpace(explicitPw)) generatedPw = password;
                    user.PasswordHash = _pw.Hash(user, password);
                    _db.Users.Add(user);
                    await _db.SaveChangesAsync();
                    wasCreated = true; created++;
                    await _audit.RecordAsync(UserId, ActorEmail, "create", "User", user.Id, user.Email);
                }
                else existing++;

                bool addedToBoard = false;
                if (board is not null && !await _db.BoardMemberships.AnyAsync(m => m.BoardId == board.Id && m.UserId == user.Id))
                {
                    _db.BoardMemberships.Add(new BoardMembership
                    {
                        BoardId = board.Id, UserId = user.Id,
                        Role = user.Role == UserRole.Teacher ? MembershipRole.Teacher : MembershipRole.Student,
                    });
                    await _db.SaveChangesAsync();
                    addedToBoard = true;
                }

                resultRows.Add(new(email, name, role.ToString(), wasCreated, addedToBoard, generatedPw, wasCreated ? null : "Already existed"));
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();   // else the row that failed to save is retried — and fails again — on every later SaveChanges
                errors++;
                resultRows.Add(new(email, name, role.ToString(), false, false, null, ex.Message));
            }
        }

        return new AdminUserImportResult(created, existing, errors, resultRows);
    }

    private static string GeneratePassword() =>
        string.Concat(Enumerable.Range(0, 10)
            .Select(_ => "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789"[System.Security.Cryptography.RandomNumberGenerator.GetInt32(54)]));

    // ---- browse all boards --------------------------------------------------
    [HttpGet("boards")]
    public async Task<ActionResult<IEnumerable<AdminBoardRow>>> Boards([FromQuery] string? q)
    {
        var query = _db.Boards.Include(b => b.Owner).Include(b => b.Members).Include(b => b.Problems).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.Trim();
            query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%") || EF.Functions.Like(b.Slug, $"%{n}%"));
        }
        var boards = await query.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync();
        return boards.Select(b => new AdminBoardRow(
            b.Id, b.Slug, b.Title, b.Owner?.Email ?? "?", b.Owner?.DisplayName ?? "?",
            b.Members.Count(m => m.Role == MembershipRole.Student), b.Problems.Count, b.CreatedAt)).ToList();
    }

    /// <summary>Same spot-check signal as the board/org-level endpoints (see
    /// PlagiarismService), scoped to any one board on the platform.</summary>
    [HttpGet("plagiarism")]
    public async Task<ActionResult<List<PlagiarismPairDto>>> Plagiarism([FromQuery] int boardId)
    {
        if (!await _db.Boards.AnyAsync(b => b.Id == boardId)) return NotFound();
        return await _plagiarism.ComputeForBoardAsync(boardId);
    }

    /// <summary>Bulk-archive (soft-delete) boards, e.g. "everything from last semester" —
    /// pick the slugs from the Boards tab. Same as deleting them one at a time: they land
    /// in Trash and can be restored/purged individually.</summary>
    [HttpPost("boards/archive")]
    public async Task<ActionResult<AdminBulkArchiveResult>> ArchiveBoards(AdminBulkArchiveDto dto)
    {
        int archived = 0;
        var errors = new List<string>();
        foreach (var slug in (dto.Slugs ?? new()).Distinct())
        {
            var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
            if (board is null) { errors.Add($"{slug}: not found"); continue; }
            board.DeletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.RecordAsync(UserId, ActorEmail, "delete", "Board", board.Id, $"{board.Title} (bulk archive)");
            archived++;
        }
        return new AdminBulkArchiveResult(archived, errors);
    }

    /// <summary>Mark a user's email verified by hand (e.g. when mail can't reach them).</summary>
    [HttpPost("users/{id:int}/verify-email")]
    public async Task<IActionResult> VerifyUserEmail(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user is null) return NotFound();
        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "email-verified-by-admin", "User", user.Id, user.Email);
        return NoContent();
    }

    /// <summary>Remove the authenticator app, email code and recovery codes from a user who lost their device and their
    /// recovery codes. They sign in with just their password afterwards (passkeys are untouched).</summary>
    [HttpPost("users/{id:int}/reset-mfa")]
    public async Task<IActionResult> ResetUserMfa(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user is null) return NotFound();
        await _mfa.ClearAllAsync(user);
        await _audit.RecordAsync(UserId, ActorEmail, "mfa-reset-by-admin", "User", user.Id, user.Email);
        return NoContent();
    }

    /// <summary>Issue a one-off password-reset link for a user, to hand over by chat or in person —
    /// works without outgoing email. Valid 24 hours, single use.</summary>
    [HttpPost("users/{id:int}/reset-link")]
    public async Task<ActionResult<AdminResetLinkDto>> ResetLink(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user is null) return NotFound();

        var (token, expires) = await _resets.CreateAsync(user, PasswordResetService.AdminLifetime);
        await _audit.RecordAsync(UserId, ActorEmail, "password-reset-link", "User", user.Id, user.Email);
        return new AdminResetLinkDto(_resets.BuildLink(Request, token), expires);
    }

    /// <summary>Every bank problem on the platform, for the bulk-add picker (the regular bank
    /// list only shows a teacher their own + public problems).</summary>
    [HttpGet("bank")]
    public async Task<ActionResult<List<BankSummaryDto>>> Bank([FromQuery] string? q)
    {
        var query = _db.BankProblems.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.Trim();
            query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%") || EF.Functions.Like(b.Tags, $"%{n}%"));
        }
        var rows = await query.Include(b => b.Owner).Include(b => b.TestCases)
            .OrderByDescending(b => b.UpdatedAt).Take(100).ToListAsync();
        return rows.Select(b => Mapping.ToSummary(b, UserId)).ToList();
    }

    /// <summary>Apply the same groups and/or bank problems to many boards at once.</summary>
    [HttpPost("boards/bulk-add")]
    public async Task<ActionResult<BulkAddResult>> BulkAdd(BulkAddDto dto)
    {
        var groups = dto.Groups ?? new();
        if (BoardBulkService.ValidateGroups(groups) is { } bad) return BadRequest(bad);
        var slugs = (dto.Slugs ?? new()).Distinct().ToList();
        if (slugs.Count == 0) return BadRequest("Select at least one board.");
        if (groups.Count == 0 && (dto.BankProblemIds ?? new()).Count == 0) return BadRequest("Nothing to add.");

        var boards = await _db.Boards.Where(b => slugs.Contains(b.Slug)).ToListAsync();
        var bankIds = (dto.BankProblemIds ?? new()).Distinct().ToList();
        var bank = await _db.BankProblems.Include(b => b.TestCases).Where(b => bankIds.Contains(b.Id)).ToListAsync();

        var rows = await _bulk.ApplyAsync(boards, groups, bank, dto.ProblemGroupTitle);
        foreach (var missing in slugs.Except(boards.Select(b => b.Slug)))
            rows.Add(new(missing, missing, 0, 0, 0, 0, "Board not found"));
        await _audit.RecordAsync(UserId, ActorEmail, "boards-bulk-add", "Board", 0,
            $"{groups.Count} group(s), {bank.Count} problem(s) → {boards.Count} board(s)");
        return new BulkAddResult(rows.Count(r => r.Error is null), rows.Count(r => r.Error is not null), rows);
    }

    // ---- system status (human-readable view of what health checks/config say) ----
    [HttpGet("system-status")]
    public async Task<ActionResult<AdminSystemStatusDto>> SystemStatus()
    {
        var judgeBackend = _judgeOpt.Queue.UseRedis ? "redis" : "in-process";
        var realtimeBackend = _realtimeOpt.UseRedis ? "redis" : "in-memory";
        bool redisConfigured = _judgeOpt.Queue.UseRedis || _realtimeOpt.UseRedis;

        bool? redisConnected = null;
        var mux = HttpContext.RequestServices.GetService<IConnectionMultiplexer>();
        if (mux is not null) redisConnected = mux.IsConnected;

        long? pendingJobs = _judgeQueue switch
        {
            InProcessJudgeQueue ip => ip.PendingJobCount,
            RedisJudgeQueue rq => await rq.PendingJobCountAsync(),
            _ => null,
        };

        bool dbOk = await _db.Database.CanConnectAsync();
        bool sysstatInstalled = await _sysstat.IsInstalledAsync();

        return new AdminSystemStatusDto(
            _toolchain.BwrapUsable ? "bubblewrap + rlimits" : "rlimits only",
            _toolchain.BwrapUsable, _judgeOpt.RequireSandbox,
            judgeBackend, realtimeBackend, redisConfigured, redisConnected,
            pendingJobs, dbOk, DateTime.UtcNow,
            _toolchain.GccVersion, _toolchain.GppVersion,
            _runtimeConfig.LspEnabled, _runtimeConfig.JudgeRateLimitMs,
            _judgeOpt.MaxConcurrent, _judgeOpt.CompileTimeoutMs, _judgeOpt.QueueCapacity,
            _lspOpt.MaxConcurrent, _lspOpt.IdleTimeoutSeconds, _lspOpt.MemoryLimitMb,
            sysstatInstalled, Environment.MachineName,
            _runtimeConfig.StudentMaxProblems, _runtimeConfig.ImageMaxKb, _runtimeConfig.ImageMaxPerUser, _runtimeConfig.NotificationReadRetentionDays,
            _runtimeConfig.LspShared, _sharedLsp.ActiveSessions, Math.Min(_sharedLsp.TotalCap, _lspOpt.SharedMaxSessions * Math.Max(1, _lspOpt.SharedMaxProcesses) * 2),
            _sharedLsp.Processes, Math.Max(1, _lspOpt.SharedMaxProcesses));
    }

    /// <summary>Today's (this host's local calendar day) CPU or memory utilization history
    /// from sysstat — see SysstatService's doc comment: this is ONE instance's own data
    /// only, never an aggregate across a multi-VM deployment. The Hostname field is there so
    /// the UI can make that visible rather than implying a fleet-wide view.</summary>
    /// <summary>How long judging takes from the student's side: submit → verdict, per hour, over the last N hours
    /// (practice + board submissions). Queue wait dominates when many students submit at once, so a high p95 in a busy
    /// hour means the judge needs more slots or CPU.</summary>
    [HttpGet("judge-latency")]
    public async Task<ActionResult<AdminJudgeLatencyDto>> JudgeLatency([FromQuery] int hours = 48)
    {
        hours = Math.Clamp(hours, 1, 168);
        var since = DateTime.UtcNow.AddHours(-hours);

        var practice = await _db.BankSubmissions
            .Where(s => s.CreatedAt >= since && s.JudgedAt != null && s.Status == SubmissionStatus.Done)
            .Select(s => new { s.CreatedAt, s.JudgedAt }).ToListAsync();
        var board = await _db.Submissions
            .Where(s => s.CreatedAt >= since && s.JudgedAt != null && s.Status == SubmissionStatus.Done)
            .Select(s => new { s.CreatedAt, s.JudgedAt }).ToListAsync();

        var rows = practice.Concat(board)
            .Select(s => (Hour: new DateTime(s.CreatedAt.Year, s.CreatedAt.Month, s.CreatedAt.Day, s.CreatedAt.Hour, 0, 0, DateTimeKind.Utc),
                          Sec: Math.Max(0, (s.JudgedAt!.Value - s.CreatedAt).TotalSeconds)))
            .ToList();

        static double P95(List<double> sorted) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)];
        var buckets = rows.GroupBy(r => r.Hour).OrderBy(g => g.Key).Select(g =>
        {
            var secs = g.Select(r => r.Sec).OrderBy(x => x).ToList();
            return new AdminJudgeLatencyBucket(g.Key, secs.Count, Math.Round(secs.Average(), 2), Math.Round(P95(secs), 2), Math.Round(secs[^1], 2));
        }).ToList();

        long? pending = _judgeQueue switch
        {
            InProcessJudgeQueue ip => ip.PendingJobCount,
            RedisJudgeQueue rq => await rq.PendingJobCountAsync(),
            _ => null,
        };
        var all = rows.Select(r => r.Sec).OrderBy(x => x).ToList();

        // The above only sees submissions that got a verdict; one whose job was lost never shows up in it.
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var stuck = (await _db.BankSubmissions.Where(s => s.CreatedAt >= since && s.CreatedAt < cutoff && s.Status != SubmissionStatus.Done)
                .Select(s => s.CreatedAt).ToListAsync())
            .Concat(await _db.Submissions.Where(s => s.CreatedAt >= since && s.CreatedAt < cutoff && s.Status != SubmissionStatus.Done)
                .Select(s => s.CreatedAt).ToListAsync()).ToList();
        var oldest = stuck.Count == 0 ? 0 : Math.Round((DateTime.UtcNow - stuck.Min()).TotalSeconds);
        return new AdminJudgeLatencyDto(hours, _judgeOpt.MaxConcurrent, pending, all.Count, Math.Round(P95(all), 2), buckets, stuck.Count, oldest);
    }

    [HttpGet("system/sysstat")]
    public async Task<ActionResult<SysstatResultDto>> Sysstat([FromQuery] string metric = "cpu")
    {
        metric = metric == "mem" ? "mem" : "cpu";
        if (!await _sysstat.IsInstalledAsync()) return NotFound("sysstat is not installed on this host.");

        var doc = await _sysstat.GetTodayAsync(metric);
        if (doc is null) return StatusCode(502, "Could not read sysstat data for today yet.");

        try
        {
            var host = doc.RootElement.GetProperty("sysstat").GetProperty("hosts")[0];
            var hostname = host.GetProperty("nodename").GetString() ?? Environment.MachineName;
            var points = new List<SysstatPointDto>();
            foreach (var stat in host.GetProperty("statistics").EnumerateArray())
            {
                var ts = stat.GetProperty("timestamp");
                var time = $"{ts.GetProperty("date").GetString()}T{ts.GetProperty("time").GetString()}Z";
                double value = metric == "mem"
                    ? stat.GetProperty("memory").GetProperty("memused-percent").GetDouble()
                    : Math.Round(100 - stat.GetProperty("cpu-load")[0].GetProperty("idle").GetDouble(), 2);
                points.Add(new SysstatPointDto(time, value));
            }
            return new SysstatResultDto(hostname, metric, points);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return StatusCode(502, "sysstat's output didn't match the expected shape (version mismatch?).");
        }
    }

    /// <summary>Who must use two-step verification, and how many accounts in each group still don't have it.
    /// Off by default; a required account without a factor is held on its Account page (MfaRequirementGate).</summary>
    [HttpGet("mfa-policy")]
    public async Task<ActionResult<AdminMfaPolicyDto>> GetMfaPolicy() => await MfaPolicyDtoAsync();

    [HttpPut("mfa-policy")]
    public async Task<ActionResult<AdminMfaPolicyDto>> SetMfaPolicy(AdminMfaPolicyDto dto)
    {
        // Don't let an admin switch on a rule that would immediately lock them out of this very page.
        var me = await _db.Users.FindAsync(UserId);
        if (me is not null && !MfaPolicy.HasFactor(me))
        {
            bool appliesToMe = (dto.RequireAdmin && _admin.IsAdminEmail(me.Email))
                || (dto.RequireTeacher && me.Role == UserRole.Teacher)
                || (dto.RequireOrgAdmin && await _db.OrganizationMemberships.AnyAsync(m => m.UserId == me.Id && m.Role == OrgRole.Admin));
            if (appliesToMe)
                return Conflict("This would apply to your own account, which has no two-step verification yet. Turn it on under Account > Security first, then switch this on.");
        }
        var row = await _db.PlatformRuntimeSettings.FindAsync(1);
        if (row is null) { row = new PlatformRuntimeSettings { Id = 1 }; _db.PlatformRuntimeSettings.Add(row); }
        row.MfaRequireAdmin = dto.RequireAdmin;
        row.MfaRequireOrgAdmin = dto.RequireOrgAdmin;
        row.MfaRequireTeacher = dto.RequireTeacher;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _runtimeConfig.SetMfaPolicy(row.MfaRequireAdmin, row.MfaRequireOrgAdmin, row.MfaRequireTeacher);
        await _audit.RecordAsync(UserId, ActorEmail, "mfa-policy-set", "RuntimeConfig", row.Id,
            $"admin={row.MfaRequireAdmin}, orgAdmin={row.MfaRequireOrgAdmin}, teacher={row.MfaRequireTeacher}");
        return await MfaPolicyDtoAsync();
    }

    private async Task<AdminMfaPolicyDto> MfaPolicyDtoAsync()
    {
        var noFactor = _db.Users.Where(u => u.DeletedAt == null && u.TotpEnabledAt == null && u.EmailMfaEnabledAt == null);
        var adminEmails = _admin.ConfiguredEmails();
        int admins = await noFactor.CountAsync(u => u.IsAdmin || adminEmails.Contains(u.Email));
        int orgAdmins = await noFactor.CountAsync(u => _db.OrganizationMemberships.Any(m => m.UserId == u.Id && m.Role == OrgRole.Admin));
        int teachers = await noFactor.CountAsync(u => u.Role == UserRole.Teacher);
        return new AdminMfaPolicyDto(_runtimeConfig.MfaRequireAdmin, _runtimeConfig.MfaRequireOrgAdmin, _runtimeConfig.MfaRequireTeacher,
            admins, orgAdmins, teachers);
    }

    /// <summary>The handful of judge/LSP knobs that are safe to flip without a restart (see
    /// PlatformRuntimeSettings/PlatformRuntimeConfig) — everything else in JudgeOptions/
    /// LspOptions (queue backend, concurrency limits, RequireSandbox) stays
    /// appsettings.json-only, since those are process-topology decisions fixed at startup.</summary>
    [HttpPut("runtime-config")]
    public async Task<ActionResult<AdminRuntimeConfigDto>> SetRuntimeConfig(AdminRuntimeConfigDto dto)
    {
        var rateLimitMs = Math.Clamp(dto.JudgeRateLimitMs, 0, 60_000);
        var studentMax = Math.Clamp(dto.StudentMaxProblems, 0, 1000);
        var imageKb = Math.Clamp(dto.ImageMaxKb, 50, 3000);   // the upload itself is capped at 3 MB
        var imagesPerUser = Math.Clamp(dto.ImageMaxPerUser, 0, 5000);
        var readDays = Math.Clamp(dto.NotificationReadRetentionDays, 0, 365);

        var row = await _db.PlatformRuntimeSettings.FindAsync(1);
        if (row is null) { row = new PlatformRuntimeSettings { Id = 1 }; _db.PlatformRuntimeSettings.Add(row); }
        row.LspEnabled = dto.LspEnabled;
        row.LspShared = dto.LspShared;
        row.JudgeRateLimitMs = rateLimitMs;
        row.StudentMaxProblems = studentMax;
        row.ImageMaxKb = imageKb;
        row.ImageMaxPerUser = imagesPerUser;
        row.NotificationReadRetentionDays = readDays;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _runtimeConfig.Set(row.LspEnabled, row.JudgeRateLimitMs);
        _runtimeConfig.SetLspShared(row.LspShared);
        _runtimeConfig.SetStudentMaxProblems(row.StudentMaxProblems);
        _runtimeConfig.SetImageLimits(row.ImageMaxKb, row.ImageMaxPerUser);
        _runtimeConfig.SetNotificationReadRetentionDays(row.NotificationReadRetentionDays);
        await _audit.RecordAsync(UserId, ActorEmail, "runtime-config-set", "RuntimeConfig", row.Id,
            $"lspEnabled={row.LspEnabled}, judgeRateLimitMs={row.JudgeRateLimitMs}, studentMaxProblems={row.StudentMaxProblems}, imageMaxKb={row.ImageMaxKb}, imageMaxPerUser={row.ImageMaxPerUser}, notificationReadRetentionDays={row.NotificationReadRetentionDays}, lspShared={row.LspShared}");
        return new AdminRuntimeConfigDto(row.LspEnabled, row.JudgeRateLimitMs, row.StudentMaxProblems, row.ImageMaxKb, row.ImageMaxPerUser, row.NotificationReadRetentionDays, row.LspShared);
    }

    /// <summary>Which notification kinds exist for everyone, and which of them may also be emailed.</summary>
    [HttpGet("notification-kinds")]
    public AdminNotifyKindsDto GetNotificationKinds() => new(
        NotificationKinds.All.Select(k => new AdminNotifyKindDto(k.Kind, k.Label,
            !_runtimeConfig.NotifyOffKinds.Contains(k.Kind), _runtimeConfig.NotifyEmailKinds.Contains(k.Kind),
            _runtimeConfig.NotifyLockedKinds.Contains(k.Kind))).ToList(),
        _email.IsConfigured);

    [HttpPut("notification-kinds")]
    public async Task<AdminNotifyKindsDto> SetNotificationKinds(List<AdminNotifyKindDto> kinds)
    {
        var off = NotificationKinds.Join(kinds.Where(k => !k.Enabled).Select(k => k.Kind));
        // "" (not null) once saved: an empty list now means "email nothing", null would mean the built-in default.
        var email = NotificationKinds.Join(kinds.Where(k => k.Enabled && k.Email).Select(k => k.Kind)) ?? "";
        var locked = NotificationKinds.Join(kinds.Where(k => k.Enabled && k.Locked).Select(k => k.Kind));
        var row = await _db.PlatformRuntimeSettings.FindAsync(1);
        if (row is null) { row = new PlatformRuntimeSettings { Id = 1 }; _db.PlatformRuntimeSettings.Add(row); }
        row.NotifyOffKinds = off;
        row.NotifyLockedKinds = locked;
        row.NotifyEmailKinds = email;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _runtimeConfig.SetNotifyKinds(row.NotifyOffKinds, row.NotifyEmailKinds, row.NotifyLockedKinds);
        await _audit.RecordAsync(UserId, ActorEmail, "notification-kinds-set", "RuntimeConfig", row.Id, $"off={off ?? "-"}, locked={locked ?? "-"}, email={(email == "" ? "-" : email)}");
        return GetNotificationKinds();
    }

    // ---- audit log ------------------------------------------------------------
    [HttpGet("audit-log")]
    public async Task<ActionResult<AdminPageDto<AdminAuditLogRow>>> AuditLogFeed(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var query = _db.AuditLogEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.Trim();
            query = query.Where(x => EF.Functions.Like(x.ActorEmail, $"%{n}%") || EF.Functions.Like(x.TargetLabel, $"%{n}%")
                || EF.Functions.Like(x.Action, $"%{n}%") || EF.Functions.Like(x.TargetType, $"%{n}%"));
        }

        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdminAuditLogRow(x.Id, x.CreatedAt, x.ActorEmail, x.Action, x.TargetType, x.TargetId, x.TargetLabel))
            .ToListAsync();

        return new AdminPageDto<AdminAuditLogRow>(rows, total, page, pageSize);
    }

    // ---- Submissions, platform-wide — every board (not just ones the admin belongs to)
    // AND every practice/bank submission, since practice has no board to be a member of.
    [HttpGet("submissions")]
    public async Task<ActionResult<AdminPageDto<AdminSubmissionRow>>> Submissions(
        [FromQuery] string? q, [FromQuery] string? verdict, [FromQuery] string? source,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Clamp(page, 1, 10_000);   // page * pageSize below must not overflow
        pageSize = Math.Clamp(pageSize, 1, 500);
        var n = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        Verdict? v = !string.IsNullOrWhiteSpace(verdict) && Enum.TryParse<Verdict>(verdict, true, out var vv) ? vv : null;
        var src = source?.Trim().ToLowerInvariant();

        // Only the newest page*pageSize of each source can land on this page, so that is all we read; the totals are counts.
        var rows = new List<AdminSubmissionRow>();
        var total = 0;
        var take = page * pageSize;

        if (src is null or "board")
        {
            var boardQuery = _db.Submissions.Where(s => s.Problem != null && s.Problem.Board != null);
            if (n is not null)
                boardQuery = boardQuery.Where(s => EF.Functions.Like(s.User!.Email, $"%{n}%")
                    || EF.Functions.Like(s.User!.DisplayName, $"%{n}%")
                    || EF.Functions.Like(s.Problem!.Title, $"%{n}%")
                    || EF.Functions.Like(s.Problem!.Board!.Title, $"%{n}%"));
            if (v is Verdict bv) boardQuery = boardQuery.Where(s => s.Verdict == bv);
            total += await boardQuery.CountAsync();
            rows.AddRange(await boardQuery.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Take(take)
                .Select(s => new AdminSubmissionRow(
                    s.Id, s.CreatedAt, s.Verdict.ToString(), s.Score, s.RuntimeMs, s.MemoryKb, s.Language,
                    s.UserId, s.User!.Email, s.User.DisplayName,
                    s.Problem!.Title, s.Problem.Board!.Slug, s.Problem.Board.Title, "Board", s.Problem.Slug))
                .ToListAsync());
        }

        if (src is null or "practice")
        {
            var bankQuery = _db.BankSubmissions.Where(s => s.BankProblem != null);
            if (n is not null)
                bankQuery = bankQuery.Where(s => EF.Functions.Like(s.User!.Email, $"%{n}%")
                    || EF.Functions.Like(s.User!.DisplayName, $"%{n}%")
                    || EF.Functions.Like(s.BankProblem!.Title, $"%{n}%"));
            if (v is Verdict pv) bankQuery = bankQuery.Where(s => s.Verdict == pv);
            total += await bankQuery.CountAsync();
            rows.AddRange(await bankQuery.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Take(take)
                .Select(s => new AdminSubmissionRow(
                    s.Id, s.CreatedAt, s.Verdict.ToString(), s.Score, s.RuntimeMs, s.MemoryKb, s.Language,
                    s.UserId, s.User!.Email, s.User.DisplayName,
                    s.BankProblem!.Title, null, null, "Practice", s.BankProblem.Slug))
                .ToListAsync());
        }

        var pageRows = rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new AdminPageDto<AdminSubmissionRow>(pageRows, total, page, pageSize);
    }

    // ---- problems export / import -------------------------------------------
    [HttpGet("problems/export")]
    public async Task<IActionResult> Export()
    {
        var problems = await _db.BankProblems.Include(b => b.Owner).Include(b => b.TestCases)
            .OrderBy(b => b.Id).ToListAsync();

        var bundle = new AdminProblemBundle(1, DateTime.UtcNow, problems.Select(b => new AdminProblemItem(
            b.Owner?.Email ?? "", b.Title, b.StatementMarkdown, b.AllowedLanguages, b.Level.ToString(),
            b.Tags, b.TimeLimitMs, b.MemoryLimitKb, b.IsPublic, b.GeneratedByAi,
            b.BannedHeaders, b.BannedSymbols,
            b.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id)
                .Select(t => new AdminProblemTest(t.Stdin, t.ExpectedStdout, t.IsSample, t.Points, t.Position))
                .ToList(),
            b.InputFileName)).ToList());

        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json",
            $"beecoding-problems-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    [HttpPost("problems/import")]
    [RequestSizeLimit(BodyLimits.AdminImportBytes)]   // a whole problem bank can be tens of MB
    public async Task<ActionResult<AdminImportResult>> Import(AdminProblemBundle bundle, [FromQuery] bool replaceExisting = true)
    {
        int created = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        var firstTeacher = await _db.Users.Where(u => u.Role == UserRole.Teacher).OrderBy(u => u.Id).FirstOrDefaultAsync();

        foreach (var item in bundle.Problems ?? new())
        {
            var title = (item.Title ?? "").Trim();
            try
            {
                if (title.Length == 0) { errors.Add("(untitled): title required"); continue; }
                if (item.Tests is null || item.Tests.Count == 0) { errors.Add($"{title}: needs at least one test"); continue; }

                var owner = string.IsNullOrWhiteSpace(item.OwnerEmail)
                    ? firstTeacher
                    : await _db.Users.FirstOrDefaultAsync(u => u.Email == item.OwnerEmail.Trim());
                owner ??= firstTeacher;
                if (owner is null) { errors.Add($"{title}: no teacher account to own it"); continue; }

                var existing = await _db.BankProblems.Include(b => b.TestCases)
                    .FirstOrDefaultAsync(b => b.OwnerId == owner.Id && b.Title == title);
                if (existing is not null && !replaceExisting) { skipped++; continue; }

                var b = existing ?? new BankProblem { OwnerId = owner.Id, CreatedAt = DateTime.UtcNow };
                b.Title = title;
                b.StatementMarkdown = item.StatementMarkdown ?? "";
                b.AllowedLanguages = Languages.Normalize(item.AllowedLanguages);
                b.Level = Mapping.ParseLevel(item.Level);
                b.Tags = Mapping.NormalizeTags(item.Tags);
                b.BannedHeaders = SourcePolicy.Normalize(item.BannedHeaders);
                b.BannedSymbols = SourcePolicy.NormalizeSymbols(item.BannedSymbols);
                b.InputFileName = InputFilePolicy.Normalize(item.InputFileName);
                b.TimeLimitMs = Math.Clamp(item.TimeLimitMs <= 0 ? 1000 : item.TimeLimitMs, 100, 10_000);
                b.MemoryLimitKb = Math.Clamp(item.MemoryLimitKb <= 0 ? 32_768 : item.MemoryLimitKb, 4_096, 512_000);
                b.IsPublic = item.IsPublic;
                b.GeneratedByAi = item.GeneratedByAi;
                b.UpdatedAt = DateTime.UtcNow;

                if (existing is not null) _db.BankTestCases.RemoveRange(existing.TestCases);
                b.TestCases = item.Tests.Select((t, i) => new BankTestCase
                {
                    Stdin = t.Stdin ?? "", ExpectedStdout = t.ExpectedStdout ?? "",
                    IsSample = t.IsSample, Points = Math.Max(0, t.Points), Position = t.Position != 0 ? t.Position : i,
                }).ToList();

                if (existing is null) { _db.BankProblems.Add(b); created++; } else updated++;
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();   // see ImportUsers
                errors.Add($"{(title.Length == 0 ? "(untitled)" : title)}: {ex.Message}");
            }
        }

        return new AdminImportResult(created, updated, skipped, errors);
    }
}
