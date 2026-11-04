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

/// <summary>Admin panel: AI usage, kill switch/quotas, provider credentials and the AI problem review queue. Same route prefix as <see cref="AdminUiController"/>.</summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui")]
public class AdminAiController(NotificationService notifications, AppDbContext db, AuditLog audit, AiRuntimeSettings aiRuntime, AiProviderRuntime aiProviderRuntime) : ApiControllerBase
{
    private readonly NotificationService _notifications = notifications;
    private readonly AppDbContext _db = db;
    private readonly AuditLog _audit = audit;
    private readonly AiRuntimeSettings _aiRuntime = aiRuntime;
    private readonly AiProviderRuntime _aiProviderRuntime = aiProviderRuntime;

    // ---- AI usage --------------------------------------------------------------
    [HttpGet("ai-usage")]
    public async Task<ActionResult<AdminAiUsagePageDto>> AiUsage(
        [FromQuery] string? sort, [FromQuery] string? dir, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var all = await AdminAnalyticsController.AiUsageAllRowsAsync(_db);
        // Whitelisted columns; default (and tiebreaker) is all-time tokens, highest first. Token and call columns
        // sort by tokens, calls break the tie.
        bool desc = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        Func<AdminAiUsageRow, AdminAiUsageBucket> bucket = sort switch
        {
            "today" => r => r.Today,
            "month" => r => r.Month,
            _ => r => r.AllTime,
        };
        if (sort == "user")
            all = (desc ? all.OrderByDescending(r => r.DisplayName, StringComparer.OrdinalIgnoreCase) : all.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
                .ThenBy(r => r.UserId).ToList();
        else
            all = (desc ? all.OrderByDescending(r => bucket(r).TotalTokens).ThenByDescending(r => bucket(r).Calls) : all.OrderBy(r => bucket(r).TotalTokens).ThenBy(r => bucket(r).Calls))
                .ThenBy(r => r.UserId).ToList();
        var pageRows = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new AdminAiUsagePageDto(pageRows, all.Count, page, pageSize);
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
        if (AiUrl.Validate(dto.BaseUrl, strict: false) is { } urlErr) return BadRequest(urlErr);
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
            b.CreatedAt, b.Id)).ToList();
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
        b.ReviewNote = null;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "ai-review-approve", "BankProblem", b.Id, b.Title);
        await NotifyOwnerAsync(b, approved: true, note: null);
        return NoContent();
    }

    /// <summary>Reject: stays in the owner's bank, privately, just never auto-shared.</summary>
    [HttpPost("ai-review/{slug}/reject")]
    public async Task<IActionResult> RejectAiReview(string slug, AdminAiReviewActionDto dto)
    {
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (!b.PendingReview) return NoContent();

        var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
        b.PendingReview = false;
        b.IsPublic = false;
        b.ReviewNote = reason is { Length: > 300 } ? reason[..300] : reason;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var label = string.IsNullOrWhiteSpace(dto.Reason) ? b.Title : $"{b.Title}: {dto.Reason}";
        await _audit.RecordAsync(UserId, ActorEmail, "ai-review-reject", "BankProblem", b.Id, label);
        await NotifyOwnerAsync(b, approved: false, note: b.ReviewNote);
        return NoContent();
    }

    /// <summary>Tell the owner how their practice submission went. A teacher's AI-generated row is queued
    /// automatically (nobody "submitted" it), so that stays silent; a student always submits explicitly.</summary>
    private async Task NotifyOwnerAsync(BankProblem b, bool approved, string? note)
    {
        if (b.GeneratedByAi && !await _db.Users.AnyAsync(u => u.Id == b.OwnerId && u.Role == UserRole.Student)) return;
        var name = await _db.Users.Where(u => u.Id == UserId).Select(u => u.DisplayName).FirstAsync();
        await _notifications.PracticeReviewedAsync(b.OwnerId, UserId, name, b.Title, approved, note);
    }

}
