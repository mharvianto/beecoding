using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
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
    AiProviderRuntime aiProviderRuntime,
    NativeToolchain toolchain, IJudgeQueue judgeQueue, IOptions<JudgeOptions> judgeOpt, IOptions<RealtimeStoreOptions> realtimeOpt)
    : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly AdminAccess _admin = admin;
    private readonly AuditLog _audit = audit;
    private readonly PasswordService _pw = pw;
    private readonly AiRuntimeSettings _aiRuntime = aiRuntime;
    private readonly AiProviderRuntime _aiProviderRuntime = aiProviderRuntime;
    private readonly NativeToolchain _toolchain = toolchain;
    private readonly IJudgeQueue _judgeQueue = judgeQueue;
    private readonly JudgeOptions _judgeOpt = judgeOpt.Value;
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

    /// <summary>Weekly active-users + submissions, for the dashboard's trend chart —
    /// same aggregation as the Reports tab's weekly-engagement export.</summary>
    [HttpGet("dashboard/weekly")]
    public async Task<ActionResult<List<AdminWeeklyStatDto>>> DashboardWeekly([FromQuery] int weeks = 12)
    {
        var agg = await EngagementAggAsync(weeks);
        return agg.Select(a => new AdminWeeklyStatDto(a.WeekStart.ToString("yyyy-MM-dd"), a.ActiveUsers, a.Submissions)).ToList();
    }

    /// <summary>Top tags by attempts, for the dashboard's ranking chart — same
    /// aggregation as the Reports tab's topic-solve-rate export.</summary>
    [HttpGet("dashboard/topics")]
    public async Task<ActionResult<List<AdminTopicStatDto>>> DashboardTopics([FromQuery] int take = 8)
    {
        take = Math.Clamp(take, 1, 50);
        var agg = await TopicsAggAsync();
        return agg.Take(take)
            .Select(a => new AdminTopicStatDto(a.Tag, a.Attempts, a.Accepted, a.Attempts > 0 ? a.Accepted / (double)a.Attempts : 0))
            .ToList();
    }

    // ---- users -------------------------------------------------------------
    [HttpGet("users")]
    public async Task<ActionResult<AdminUserPageDto>> Users(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Users.Where(u => u.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.Trim();
            query = query.Where(u => EF.Functions.Like(u.Email, $"%{n}%") || EF.Functions.Like(u.DisplayName, $"%{n}%"));
        }

        var total = await query.CountAsync();
        var users = await query.OrderBy(u => u.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var ids = users.Select(u => u.Id).ToList();
        var ownedByUser = await _db.Boards.Where(b => ids.Contains(b.OwnerId)).GroupBy(b => b.OwnerId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var subsByUser = await _db.Submissions.Where(s => ids.Contains(s.UserId)).GroupBy(s => s.UserId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);

        var rows = users.Select(u => new AdminUserRow(
            u.Id, u.Email, u.DisplayName, u.Role.ToString(), _admin.IsAdminEmail(u.Email),
            u.Xp, u.CreatedAt,
            ownedByUser.GetValueOrDefault(u.Id), subsByUser.GetValueOrDefault(u.Id))).ToList();

        return new AdminUserPageDto(rows, total, page, pageSize);
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
        var rows = ParseCsv(dto.Csv ?? "");
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
            var name = Get(nameCol);
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

            try
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
                bool wasCreated = false;
                string? generatedPw = null;
                if (user is null)
                {
                    user = new User { Email = email, DisplayName = name, Role = role };
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
                errors++;
                resultRows.Add(new(email, name, role.ToString(), false, false, null, ex.Message));
            }
        }

        return new AdminUserImportResult(created, existing, errors, resultRows);
    }

    private static string GeneratePassword() =>
        string.Concat(Enumerable.Range(0, 10)
            .Select(_ => "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789"[Random.Shared.Next(54)]));

    /// <summary>Minimal RFC-4180-ish CSV parser: quoted fields, "" for an escaped quote.</summary>
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQuotes = false;
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }

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

        return new AdminSystemStatusDto(
            _toolchain.BwrapUsable ? "bubblewrap + rlimits" : "rlimits only",
            _toolchain.BwrapUsable, _judgeOpt.RequireSandbox,
            judgeBackend, realtimeBackend, redisConfigured, redisConnected,
            pendingJobs, dbOk, DateTime.UtcNow);
    }

    // ---- trash: browse + restore/purge soft-deleted rows ---------------------
    /// <summary>One trash category at a time, paginated — kind is "users" | "boards" |
    /// "problems" | "bank".</summary>
    [HttpGet("trash/{kind}")]
    public async Task<IActionResult> Trash(string kind, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var n = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        switch (kind)
        {
            case "users":
            {
                var query = _db.Users.Where(u => u.DeletedAt != null);
                if (n is not null)
                    query = query.Where(u => EF.Functions.Like(u.Email, $"%{n}%") || EF.Functions.Like(u.DisplayName, $"%{n}%"));
                var total = await query.CountAsync();
                var rows = await query.OrderByDescending(u => u.DeletedAt).Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(u => new AdminTrashUserRow(u.Id, u.Email, u.DisplayName, u.DeletedAt!.Value)).ToListAsync();
                return Ok(new AdminPageDto<AdminTrashUserRow>(rows, total, page, pageSize));
            }
            case "boards":
            {
                var query = _db.Boards.IgnoreQueryFilters().Where(b => b.DeletedAt != null);
                if (n is not null) query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%"));
                var total = await query.CountAsync();
                var rows = await query.Include(b => b.Owner).OrderByDescending(b => b.DeletedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(b => new AdminTrashBoardRow(b.Slug, b.Title, b.Owner != null ? b.Owner.Email : "?", b.DeletedAt!.Value))
                    .ToListAsync();
                return Ok(new AdminPageDto<AdminTrashBoardRow>(rows, total, page, pageSize));
            }
            case "problems":
            {
                var query = _db.Problems.IgnoreQueryFilters().Where(p => p.DeletedAt != null);
                if (n is not null) query = query.Where(p => EF.Functions.Like(p.Title, $"%{n}%"));
                var total = await query.CountAsync();
                var problems = await query.OrderByDescending(p => p.DeletedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                var boardIds = problems.Select(p => p.BoardId).Distinct().ToList();
                var boardById = await _db.Boards.IgnoreQueryFilters().Where(b => boardIds.Contains(b.Id))
                    .Select(b => new { b.Id, b.Slug, b.Title }).ToDictionaryAsync(b => b.Id);
                var rows = problems.Select(p =>
                {
                    boardById.TryGetValue(p.BoardId, out var b);
                    return new AdminTrashProblemRow(p.Slug, p.Title, b?.Slug ?? "", b?.Title ?? "(deleted board)", p.DeletedAt!.Value);
                }).ToList();
                return Ok(new AdminPageDto<AdminTrashProblemRow>(rows, total, page, pageSize));
            }
            case "bank":
            {
                var query = _db.BankProblems.IgnoreQueryFilters().Where(b => b.DeletedAt != null);
                if (n is not null) query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%"));
                var total = await query.CountAsync();
                var rows = await query.Include(b => b.Owner).OrderByDescending(b => b.DeletedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(b => new AdminTrashBankRow(b.Slug, b.Title, b.Owner != null ? b.Owner.Email : "?", b.DeletedAt!.Value))
                    .ToListAsync();
                return Ok(new AdminPageDto<AdminTrashBankRow>(rows, total, page, pageSize));
            }
            default:
                return BadRequest("kind must be users, boards, problems, or bank.");
        }
    }

    private static readonly string[] TrashKinds = { "users", "boards", "problems", "bank" };

    /// <summary>Bulk restore, one trash category at a time — ids are user ids (users) or
    /// slugs (boards/problems/bank), matching what the Trash tab's rows carry.</summary>
    [HttpPost("trash/{kind}/restore")]
    public async Task<ActionResult<AdminTrashBulkResult>> BulkRestoreTrash(string kind, AdminTrashBulkDto dto)
    {
        if (!TrashKinds.Contains(kind)) return BadRequest("kind must be users, boards, problems, or bank.");

        int count = 0;
        var errors = new List<string>();
        foreach (var id in (dto.Ids ?? new()).Distinct())
        {
            try
            {
                switch (kind)
                {
                    case "users":
                        if (!int.TryParse(id, out var uid)) { errors.Add($"{id}: invalid id"); continue; }
                        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
                        if (user is null) { errors.Add($"{id}: not found"); continue; }
                        if (user.DeletedAt is null) continue;
                        user.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "User", user.Id, $"{user.Email} (bulk)");
                        break;
                    case "boards":
                        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == id);
                        if (board is null) { errors.Add($"{id}: not found"); continue; }
                        if (board.DeletedAt is null) continue;
                        board.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "Board", board.Id, $"{board.Title} (bulk)");
                        break;
                    case "problems":
                        var p = await _db.Problems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (p is null) { errors.Add($"{id}: not found"); continue; }
                        if (p.DeletedAt is null) continue;
                        p.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "Problem", p.Id, $"{p.Title} (bulk)");
                        break;
                    case "bank":
                        var bp = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (bp is null) { errors.Add($"{id}: not found"); continue; }
                        if (bp.DeletedAt is null) continue;
                        bp.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "BankProblem", bp.Id, $"{bp.Title} (bulk)");
                        break;
                }
                count++;
            }
            catch (Exception ex) { errors.Add($"{id}: {ex.Message}"); }
        }
        return new AdminTrashBulkResult(count, errors);
    }

    /// <summary>Bulk permanent purge, one trash category at a time.</summary>
    [HttpPost("trash/{kind}/purge")]
    public async Task<ActionResult<AdminTrashBulkResult>> BulkPurgeTrash(string kind, AdminTrashBulkDto dto)
    {
        if (!TrashKinds.Contains(kind)) return BadRequest("kind must be users, boards, problems, or bank.");

        int count = 0;
        var errors = new List<string>();
        foreach (var id in (dto.Ids ?? new()).Distinct())
        {
            try
            {
                switch (kind)
                {
                    case "users":
                        if (!int.TryParse(id, out var uid)) { errors.Add($"{id}: invalid id"); continue; }
                        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
                        if (user is null) { errors.Add($"{id}: not found"); continue; }
                        if (user.DeletedAt is null) { errors.Add($"{user.Email}: delete it first"); continue; }
                        if (await _db.Boards.IgnoreQueryFilters().AnyAsync(b => b.OwnerId == uid))
                        { errors.Add($"{user.Email}: still owns board(s)"); continue; }
                        _db.Users.Remove(user);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "User", uid, $"{user.Email} (bulk)");
                        break;
                    case "boards":
                        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == id);
                        if (board is null) { errors.Add($"{id}: not found"); continue; }
                        if (board.DeletedAt is null) { errors.Add($"{board.Title}: delete it first"); continue; }
                        _db.Boards.Remove(board);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Board", board.Id, $"{board.Title} (bulk)");
                        break;
                    case "problems":
                        var p = await _db.Problems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (p is null) { errors.Add($"{id}: not found"); continue; }
                        if (p.DeletedAt is null) { errors.Add($"{p.Title}: delete it first"); continue; }
                        _db.Problems.Remove(p);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Problem", p.Id, $"{p.Title} (bulk)");
                        break;
                    case "bank":
                        var bp = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (bp is null) { errors.Add($"{id}: not found"); continue; }
                        if (bp.DeletedAt is null) { errors.Add($"{bp.Title}: delete it first"); continue; }
                        _db.BankProblems.Remove(bp);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "BankProblem", bp.Id, $"{bp.Title} (bulk)");
                        break;
                }
                count++;
            }
            catch (Exception ex) { errors.Add($"{id}: {ex.Message}"); }
        }
        return new AdminTrashBulkResult(count, errors);
    }

    [HttpDelete("trash/users/{id:int}")]
    public async Task<IActionResult> PurgeUser(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");
        if (await _db.Boards.IgnoreQueryFilters().AnyAsync(b => b.OwnerId == id))
            return Conflict("This user still owns board(s) — delete/purge those first.");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "User", id, user.Email);
        return NoContent();
    }

    [HttpDelete("trash/boards/{slug}")]
    public async Task<IActionResult> PurgeBoard(string slug)
    {
        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        if (board.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Board", board.Id, board.Title);
        return NoContent();
    }

    [HttpDelete("trash/problems/{slug}")]
    public async Task<IActionResult> PurgeProblem(string slug)
    {
        var p = await _db.Problems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == slug);
        if (p is null) return NotFound();
        if (p.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");

        _db.Problems.Remove(p);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Problem", p.Id, p.Title);
        return NoContent();
    }

    [HttpDelete("trash/bank/{slug}")]
    public async Task<IActionResult> PurgeBankProblem(string slug)
    {
        var b = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");

        _db.BankProblems.Remove(b);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "BankProblem", b.Id, b.Title);
        return NoContent();
    }

    // ---- Organizations (multi-tenant) ---------------------------------------
    /// <summary>Creating/deleting an organization is platform-super-admin only — everything
    /// else about running one (members, boards, AI settings) is OrgAdminController, which a
    /// super admin can also call for any org.</summary>
    [HttpGet("organizations")]
    public async Task<ActionResult<List<OrganizationDto>>> Organizations()
    {
        return await _db.Organizations.OrderBy(o => o.Name)
            .Select(o => new OrganizationDto(o.Id, o.Name, o.Slug, o.CreatedAt)).ToListAsync();
    }

    [HttpPost("organizations")]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization(AdminUpsertOrganizationDto dto)
    {
        var name = (dto.Name ?? "").Trim();
        var slug = (dto.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0 || slug.Length == 0) return BadRequest("Name and slug are required.");
        if (await _db.Organizations.AnyAsync(o => o.Slug == slug)) return Conflict("That slug is already used.");

        var org = new Organization { Name = name, Slug = slug };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "create", "Organization", org.Id, org.Name);
        return new OrganizationDto(org.Id, org.Name, org.Slug, org.CreatedAt);
    }

    [HttpDelete("organizations/{id:int}")]
    public async Task<IActionResult> DeleteOrganization(int id)
    {
        var org = await _db.Organizations.FindAsync(id);
        if (org is null) return NotFound();
        _db.Organizations.Remove(org);   // memberships/AiSettings cascade; boards/LtiPlatforms just detach (SetNull)
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "Organization", id, org.Name);
        return NoContent();
    }

    // ---- LTI 1.3 platform registry ------------------------------------------
    /// <summary>Values an LMS admin needs to register BeeCoding as an external tool.</summary>
    [HttpGet("lti-platforms/tool-config")]
    public ActionResult<AdminLtiToolConfigDto> LtiToolConfig()
    {
        string Abs(string path) => $"{Request.Scheme}://{Request.Host}{Url.Content("~" + path)}";
        return new AdminLtiToolConfigDto(
            LoginInitiationUrl: Abs("/lti/login"),
            LaunchUrl: Abs("/lti/launch"),
            JwksUrl: Abs("/lti/jwks"),
            DeepLinkingUrl: Abs("/lti/launch"));   // same endpoint — discriminated by message_type
    }

    [HttpGet("lti-platforms")]
    public async Task<ActionResult<List<AdminLtiPlatformDto>>> LtiPlatforms()
    {
        return await _db.LtiPlatforms.Include(p => p.Organization).OrderBy(p => p.Name).Select(p => new AdminLtiPlatformDto(
            p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt,
            p.OrganizationId, p.Organization != null ? p.Organization.Name : null))
            .ToListAsync();
    }

    [HttpPost("lti-platforms")]
    public async Task<ActionResult<AdminLtiPlatformDto>> CreateLtiPlatform(AdminUpsertLtiPlatformDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Issuer) || string.IsNullOrWhiteSpace(dto.ClientId))
            return BadRequest("Issuer and Client ID are required.");
        var p = new LtiPlatform
        {
            Name = dto.Name.Trim(), Issuer = dto.Issuer.Trim(), ClientId = dto.ClientId.Trim(),
            DeploymentIds = dto.DeploymentIds.Trim(), AuthLoginUrl = dto.AuthLoginUrl.Trim(),
            AuthTokenUrl = dto.AuthTokenUrl.Trim(), JwksUrl = dto.JwksUrl.Trim(), Enabled = dto.Enabled,
            OrganizationId = dto.OrganizationId,
        };
        _db.LtiPlatforms.Add(p);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "create", "LtiPlatform", p.Id, p.Name);
        var orgName = dto.OrganizationId is int oid ? await _db.Organizations.Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync() : null;
        return new AdminLtiPlatformDto(p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt, p.OrganizationId, orgName);
    }

    [HttpPut("lti-platforms/{id:int}")]
    public async Task<ActionResult<AdminLtiPlatformDto>> UpdateLtiPlatform(int id, AdminUpsertLtiPlatformDto dto)
    {
        var p = await _db.LtiPlatforms.FindAsync(id);
        if (p is null) return NotFound();
        p.Name = dto.Name.Trim(); p.Issuer = dto.Issuer.Trim(); p.ClientId = dto.ClientId.Trim();
        p.DeploymentIds = dto.DeploymentIds.Trim(); p.AuthLoginUrl = dto.AuthLoginUrl.Trim();
        p.AuthTokenUrl = dto.AuthTokenUrl.Trim(); p.JwksUrl = dto.JwksUrl.Trim(); p.Enabled = dto.Enabled;
        p.OrganizationId = dto.OrganizationId;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "update", "LtiPlatform", p.Id, p.Name);
        var orgName = dto.OrganizationId is int oid ? await _db.Organizations.Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync() : null;
        return new AdminLtiPlatformDto(p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt, p.OrganizationId, orgName);
    }

    [HttpDelete("lti-platforms/{id:int}")]
    public async Task<IActionResult> DeleteLtiPlatform(int id)
    {
        var p = await _db.LtiPlatforms.FindAsync(id);
        if (p is null) return NotFound();
        _db.LtiPlatforms.Remove(p);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "LtiPlatform", id, p.Name);
        return NoContent();
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

    // ---- AI usage --------------------------------------------------------------
    [HttpGet("ai-usage")]
    public async Task<ActionResult<IEnumerable<AdminAiUsageRow>>> AiUsage()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var rows = await _db.AiUsages.Include(x => x.User).ToListAsync();

        static AdminAiUsageBucket Sum(IEnumerable<AiUsage> xs)
        {
            int c = 0; long p = 0, k = 0;
            foreach (var x in xs) { c += x.Calls; p += x.PromptTokens; k += x.CompletionTokens; }
            return new AdminAiUsageBucket(c, p, k, p + k);
        }

        return rows.GroupBy(x => x.UserId)
            .Select(g => new AdminAiUsageRow(
                g.Key,
                g.First().User?.Email ?? "?",
                g.First().User?.DisplayName ?? "?",
                Sum(g.Where(x => x.Day == today)),
                Sum(g.Where(x => x.Day >= monthStart)),
                Sum(g)))
            .OrderByDescending(r => r.AllTime.TotalTokens)
            .ToList();
    }

    // ---- AI kill-switch, quotas, per-user overrides -------------------------
    [HttpGet("ai-settings")]
    public async Task<ActionResult<AiGlobalSettingsDto>> GetAiSettings()
    {
        var s = await _db.AiSettings.FindAsync(1) ?? new AiSettings();
        return new AiGlobalSettingsDto(s.Paused, s.PausedReason, s.DailyQuotaStudent, s.DailyQuotaTeacher);
    }

    /// <summary>Global pause ("kill switch") + default daily quota per role. Takes effect
    /// on the very next AI request across the whole app — no restart.</summary>
    [HttpPut("ai-settings")]
    public async Task<ActionResult<AiGlobalSettingsDto>> SetAiSettings(AiGlobalSettingsDto dto)
    {
        var quotaStudent = Math.Max(0, dto.DailyQuotaStudent);
        var quotaTeacher = Math.Max(0, dto.DailyQuotaTeacher);
        var reason = string.IsNullOrWhiteSpace(dto.PausedReason) ? null : dto.PausedReason.Trim();

        var s = await _db.AiSettings.FindAsync(1);
        if (s is null) { s = new AiSettings { Id = 1 }; _db.AiSettings.Add(s); }
        var wasPaused = s.Paused;
        s.Paused = dto.Paused;
        s.PausedReason = reason;
        s.DailyQuotaStudent = quotaStudent;
        s.DailyQuotaTeacher = quotaTeacher;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _aiRuntime.SetGlobal(dto.Paused, reason, quotaStudent, quotaTeacher);
        if (dto.Paused != wasPaused)
            await _audit.RecordAsync(UserId, ActorEmail, dto.Paused ? "ai-pause" : "ai-resume", "AiSettings", 1, reason ?? "");

        return new AiGlobalSettingsDto(s.Paused, s.PausedReason, s.DailyQuotaStudent, s.DailyQuotaTeacher);
    }

    // ---- AI provider/credential: platform default -----------------------------
    [HttpGet("ai-provider")]
    public async Task<ActionResult<AiProviderConfigDto>> GetAiProvider()
    {
        var row = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == null);
        return new AiProviderConfigDto(!string.IsNullOrEmpty(row?.ApiKey), ApiKeyPreview(row?.ApiKey), row?.BaseUrl, row?.Model, row?.GenerateModel);
    }

    /// <summary>Overrides appsettings.json's Ai:* at runtime, no restart needed — leave
    /// ApiKey blank to keep whatever key is already saved (it's never sent back to the
    /// client); use DELETE ai-provider/api-key to actually clear it.</summary>
    [HttpPut("ai-provider")]
    public async Task<ActionResult<AiProviderConfigDto>> SetAiProvider(AiSetProviderConfigDto dto)
    {
        var row = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == null);
        if (row is null) { row = new AiProviderConfig(); _db.AiProviderConfigs.Add(row); }
        if (!string.IsNullOrWhiteSpace(dto.ApiKey)) row.ApiKey = dto.ApiKey.Trim();
        row.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim();
        row.Model = string.IsNullOrWhiteSpace(dto.Model) ? null : dto.Model.Trim();
        row.GenerateModel = string.IsNullOrWhiteSpace(dto.GenerateModel) ? null : dto.GenerateModel.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _aiProviderRuntime.SetPlatform(row.ApiKey, row.BaseUrl, row.Model, row.GenerateModel);
        await _audit.RecordAsync(UserId, ActorEmail, "ai-provider-set", "AiProviderConfig", row.Id, "platform default");
        return new AiProviderConfigDto(!string.IsNullOrEmpty(row.ApiKey), ApiKeyPreview(row.ApiKey), row.BaseUrl, row.Model, row.GenerateModel);
    }

    [HttpDelete("ai-provider/api-key")]
    public async Task<IActionResult> ClearAiProviderKey()
    {
        var row = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == null);
        if (row is null || row.ApiKey is null) return NoContent();
        row.ApiKey = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _aiProviderRuntime.SetPlatform(null, row.BaseUrl, row.Model, row.GenerateModel);
        await _audit.RecordAsync(UserId, ActorEmail, "ai-provider-clear-key", "AiProviderConfig", row.Id, "platform default");
        return NoContent();
    }

    private static string? ApiKeyPreview(string? key) =>
        string.IsNullOrEmpty(key) ? null : $"••••{key[Math.Max(0, key.Length - 4)..]}";

    [HttpGet("ai-settings/overrides")]
    public async Task<ActionResult<IEnumerable<AiUserOverrideDto>>> GetAiOverrides()
    {
        var rows = await _db.AiUserSettings.Include(x => x.User)
            .Where(x => x.Banned || x.DailyQuotaOverride != null)
            .ToListAsync();
        return rows
            .Select(x => new AiUserOverrideDto(x.UserId, x.User?.Email ?? "?", x.User?.DisplayName ?? "?", x.DailyQuotaOverride, x.Banned))
            .OrderBy(x => x.Email)
            .ToList();
    }

    /// <summary>Set a per-user daily-quota override and/or ban. Takes effect immediately.</summary>
    [HttpPut("ai-settings/overrides/{userId:int}")]
    public async Task<IActionResult> SetAiOverride(int userId, AiSetUserOverrideDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        var quota = dto.DailyQuotaOverride is int q ? Math.Max(0, q) : (int?)null;
        var row = await _db.AiUserSettings.FirstOrDefaultAsync(x => x.UserId == userId);
        if (row is null) { row = new AiUserSetting { UserId = userId }; _db.AiUserSettings.Add(row); }
        row.DailyQuotaOverride = quota;
        row.Banned = dto.Banned;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _aiRuntime.SetOverride(userId, quota, dto.Banned);
        await _audit.RecordAsync(UserId, ActorEmail, dto.Banned ? "ai-ban" : "ai-quota-set", "User", userId,
            dto.Banned ? user.Email : $"{user.Email}: quota={quota?.ToString() ?? "(role default)"}");
        return NoContent();
    }

    [HttpDelete("ai-settings/overrides/{userId:int}")]
    public async Task<IActionResult> ClearAiOverride(int userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        var row = await _db.AiUserSettings.FirstOrDefaultAsync(x => x.UserId == userId);
        if (row is not null)
        {
            _db.AiUserSettings.Remove(row);
            await _db.SaveChangesAsync();
        }
        _aiRuntime.ClearOverride(userId);
        await _audit.RecordAsync(UserId, ActorEmail, "ai-override-clear", "User", userId, user.Email);
        return NoContent();
    }

    // ---- AI-generated problem review queue -----------------------------------
    // An AI-generated bank problem is held back (IsPublic=false, PendingReview=true) until
    // an admin approves it — the point isn't to gate the OWNER's own use of it (they can
    // already use/edit it privately the moment it's generated), only whether it's safe to
    // spread, unreviewed, into every other teacher's bank.
    [HttpGet("ai-review")]
    public async Task<ActionResult<IEnumerable<AdminAiReviewRow>>> AiReviewQueue()
    {
        var rows = await _db.BankProblems.Include(b => b.Owner).Include(b => b.TestCases)
            .Where(b => b.PendingReview)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        return rows.Select(b => new AdminAiReviewRow(
            b.Slug, b.Title, b.Owner?.Email ?? "?", b.Owner?.DisplayName ?? "?", b.Level.ToString(), b.Tags,
            b.AllowedLanguages, b.StatementMarkdown, b.TimeLimitMs, b.MemoryLimitKb,
            b.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id)
                .Select(t => new AdminAiReviewTest(t.Stdin, t.ExpectedStdout, t.IsSample)).ToList(),
            b.CreatedAt)).ToList();
    }

    /// <summary>Approve: publish it to the shared bank, as originally intended.</summary>
    [HttpPost("ai-review/{slug}/approve")]
    public async Task<IActionResult> ApproveAiReview(string slug)
    {
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (!b.PendingReview) return NoContent();

        b.PendingReview = false;
        b.IsPublic = true;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "ai-review-approve", "BankProblem", b.Id, b.Title);
        return NoContent();
    }

    /// <summary>Reject: stays in the owner's bank, privately, just never auto-shared.</summary>
    [HttpPost("ai-review/{slug}/reject")]
    public async Task<IActionResult> RejectAiReview(string slug, AdminAiReviewActionDto dto)
    {
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (!b.PendingReview) return NoContent();

        b.PendingReview = false;
        b.IsPublic = false;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var label = string.IsNullOrWhiteSpace(dto.Reason) ? b.Title : $"{b.Title}: {dto.Reason}";
        await _audit.RecordAsync(UserId, ActorEmail, "ai-review-reject", "BankProblem", b.Id, label);
        return NoContent();
    }

    // ---- analytics export (CSV + Excel) — also usable as grant/report evidence ----
    [HttpGet("analytics/topics.csv")]
    public async Task<IActionResult> TopicsCsv() => CsvFile(await TopicsRowsAsync(), "topic-stats.csv");

    [HttpGet("analytics/users.csv")]
    public async Task<IActionResult> UsersCsv() => CsvFile(await UsersRowsAsync(), "user-xp.csv");

    [HttpGet("analytics/engagement.csv")]
    public async Task<IActionResult> EngagementCsv([FromQuery] int weeks = 12) => CsvFile(await EngagementRowsAsync(weeks), "weekly-engagement.csv");

    [HttpGet("analytics/report.xlsx")]
    public async Task<IActionResult> ReportXlsx([FromQuery] int weeks = 12)
    {
        using var wb = new XLWorkbook();
        WriteSheet(wb, "Topics", await TopicsRowsAsync());
        WriteSheet(wb, "Users", await UsersRowsAsync());
        WriteSheet(wb, "Weekly engagement", await EngagementRowsAsync(weeks));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "beecoding-report.xlsx");
    }

    private static void WriteSheet(XLWorkbook wb, string sheetName, List<string[]> rows)
    {
        var ws = wb.Worksheets.Add(sheetName);
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 1, c + 1).Value = rows[r][c];
        if (rows.Count > 0) ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private async Task<List<TopicAgg>> TopicsAggAsync()
    {
        var boardProblems = await _db.Problems.Select(p => new { p.Id, p.Tags }).ToListAsync();
        var bankProblems = await _db.BankProblems.Select(p => new { p.Id, p.Tags }).ToListAsync();
        var boardSubs = await _db.Submissions.Select(s => new { s.ProblemId, s.UserId, s.Verdict, s.Score }).ToListAsync();
        var bankSubs = await _db.BankSubmissions.Select(s => new { s.BankProblemId, s.UserId, s.Verdict, s.Score }).ToListAsync();

        var byTag = new Dictionary<string, TopicAgg>();
        TopicAgg Agg(string tag) { if (!byTag.TryGetValue(tag, out var a)) byTag[tag] = a = new TopicAgg { Tag = tag }; return a; }
        bool Solved(Verdict v, double score) => v == Verdict.Accepted && score >= 1.0;

        foreach (var p in boardProblems)
        {
            var subs = boardSubs.Where(s => s.ProblemId == p.Id).ToList();
            foreach (var tag in TagsOf(p.Tags))
            {
                var a = Agg(tag);
                a.ProblemIds.Add(("board", p.Id));
                a.Attempts += subs.Count;
                foreach (var s in subs.Where(s => Solved(s.Verdict, s.Score))) { a.Accepted++; a.Solvers.Add(s.UserId); }
            }
        }
        foreach (var p in bankProblems)
        {
            var subs = bankSubs.Where(s => s.BankProblemId == p.Id).ToList();
            foreach (var tag in TagsOf(p.Tags))
            {
                var a = Agg(tag);
                a.ProblemIds.Add(("bank", p.Id));
                a.Attempts += subs.Count;
                foreach (var s in subs.Where(s => Solved(s.Verdict, s.Score))) { a.Accepted++; a.Solvers.Add(s.UserId); }
            }
        }

        return byTag.Values.OrderByDescending(a => a.Attempts).ToList();
    }

    private async Task<List<string[]>> TopicsRowsAsync()
    {
        var agg = await TopicsAggAsync();
        var rows = new List<string[]> { new[] { "Tag", "Problems", "Attempts", "AcceptedSubmissions", "AcceptRate", "DistinctSolvers" } };
        foreach (var a in agg)
        {
            var rate = a.Attempts > 0 ? (a.Accepted / (double)a.Attempts).ToString("0.00") : "";
            rows.Add(new[] { a.Tag, a.ProblemIds.Count.ToString(), a.Attempts.ToString(), a.Accepted.ToString(), rate, a.Solvers.Count.ToString() });
        }
        return rows;
    }

    private sealed class TopicAgg
    {
        public string Tag { get; set; } = "";
        public HashSet<(string Kind, int Id)> ProblemIds { get; } = new();
        public int Attempts;
        public int Accepted;
        public HashSet<int> Solvers { get; } = new();
    }

    private async Task<List<string[]>> UsersRowsAsync()
    {
        var users = await _db.Users.OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.Xp, u.CreatedAt }).ToListAsync();
        var rows = new List<string[]> { new[] { "Id", "Email", "DisplayName", "Role", "Xp", "Level", "CreatedAt" } };
        rows.AddRange(users.Select(u => new[]
        {
            u.Id.ToString(), u.Email, u.DisplayName, u.Role.ToString(),
            u.Xp.ToString(), ProgressService.LevelForXp(u.Xp).ToString(), u.CreatedAt.ToString("O"),
        }));
        return rows;
    }

    private async Task<List<(DateOnly WeekStart, int ActiveUsers, int Submissions)>> EngagementAggAsync(int weeks)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        var boardActivity = await _db.Submissions.Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();
        var bankActivity = await _db.BankSubmissions.Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();
        var all = boardActivity.Select(x => (x.UserId, x.CreatedAt)).Concat(bankActivity.Select(x => (x.UserId, x.CreatedAt)));

        DateOnly WeekStart(DateTime dt)
        {
            var d = DateOnly.FromDateTime(dt);
            return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));   // Monday of that week
        }

        return all.GroupBy(x => WeekStart(x.CreatedAt))
            .OrderByDescending(g => g.Key).Take(weeks).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Select(x => x.UserId).Distinct().Count(), g.Count()))
            .ToList();
    }

    private async Task<List<string[]>> EngagementRowsAsync(int weeks)
    {
        var agg = await EngagementAggAsync(weeks);
        var rows = new List<string[]> { new[] { "WeekStart", "ActiveUsers", "TotalSubmissions" } };
        rows.AddRange(agg.Select(a => new[] { a.WeekStart.ToString("yyyy-MM-dd"), a.ActiveUsers.ToString(), a.Submissions.ToString() }));
        return rows;
    }

    private FileContentResult CsvFile(IEnumerable<string[]> rows, string fileName)
    {
        var sb = new StringBuilder();
        foreach (var row in rows) sb.AppendLine(string.Join(',', row.Select(CsvEscape)));
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    private static string CsvEscape(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    private static IEnumerable<string> TagsOf(string? t) =>
        (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(x => x.ToLowerInvariant()).Distinct();

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
            catch (Exception ex) { errors.Add($"{(title.Length == 0 ? "(untitled)" : title)}: {ex.Message}"); }
        }

        return new AdminImportResult(created, updated, skipped, errors);
    }
}
