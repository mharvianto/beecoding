using BeeCoding.Controllers;
using BeeCoding.Data;
using BeeCoding.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Fills the in-memory copies of admin-edited settings (admin emails, AI pause/quotas/providers, LTI origins, runtime config)
/// from the database. Runs once at startup; with several web instances <see cref="RuntimeCacheRefresher"/> runs it again every few
/// seconds, so a change an admin saves on one instance reaches the others without a restart.
/// </summary>
public static class RuntimeCacheLoader
{
    public static async Task LoadAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        sp.GetRequiredService<AdminAccess>().SetDbAdmins(await db.Users.Where(u => u.IsAdmin).Select(u => u.Email).ToListAsync(ct));

        var ai = sp.GetRequiredService<AiRuntimeSettings>();
        var aiSettings = await db.AiSettings.FindAsync([1], ct);
        if (aiSettings is not null) ai.SetGlobal(aiSettings.Paused, aiSettings.PausedReason, aiSettings.DailyQuotaStudent, aiSettings.DailyQuotaTeacher);
        var overrides = await db.AiUserSettings.Select(x => new { x.UserId, x.DailyQuotaOverride, x.Banned }).ToListAsync(ct);
        ai.SetOverrides(overrides.Select(x => (x.UserId, x.DailyQuotaOverride, x.Banned)));
        var orgAi = await db.AiSettings.Where(x => x.OrganizationId != null)
            .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.Paused, x.PausedReason, x.DailyQuotaStudent, x.DailyQuotaTeacher })
            .ToListAsync(ct);
        ai.SetOrgs(orgAi.Select(x => (x.OrganizationId, x.Paused, x.PausedReason, x.DailyQuotaStudent, x.DailyQuotaTeacher)));

        // appsettings.json's Ai:* stays the ultimate fallback when neither layer has a row.
        var provider = sp.GetRequiredService<AiProviderRuntime>();
        var platform = await db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == null, ct);
        if (platform is not null)
        {
            provider.SetPlatform(platform.ApiKey, platform.BaseUrl, platform.Model, platform.GenerateModel);
            provider.SetExtras(platform.MaxTokens, platform.ReasoningEffort, Fallbacks(platform.Fallbacks), platform.ReviewModel, platform.JobEffort);
        }
        var orgs = await db.AiProviderConfigs.Where(x => x.OrganizationId != null)
            .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.ApiKey, x.BaseUrl, x.Model, x.GenerateModel, x.MaxTokens, x.ReasoningEffort, x.Fallbacks })
            .ToListAsync(ct);
        provider.SetOrgs(orgs.Select(x => (x.OrganizationId, x.ApiKey, x.BaseUrl, x.Model, x.GenerateModel)));
        provider.SetOrgsExtras(orgs.Select(x => (x.OrganizationId, x.MaxTokens, x.ReasoningEffort, Fallbacks(x.Fallbacks))));

        // Which origins the CSP lets frame this app: every enabled LTI platform's issuer.
        sp.GetRequiredService<LtiPlatformOriginsCache>().Set(await db.LtiPlatforms.Where(p => p.Enabled).Select(p => p.Issuer).ToListAsync(ct));

        var row = await db.PlatformRuntimeSettings.FindAsync([1], ct);
        if (row is null) return;
        var rc = sp.GetRequiredService<PlatformRuntimeConfig>();
        rc.Set(row.LspEnabled, row.JudgeRateLimitMs);
        rc.SetLspShared(row.LspShared);
        rc.SetLspDefaultOn(row.LspDefaultOn);
        rc.ApplyLspLimits(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BeeCoding.Services.Lsp.LspOptions>>().Value, row);
        rc.SetStudentMaxProblems(row.StudentMaxProblems);
        rc.SetImageLimits(row.ImageMaxKb, row.ImageMaxPerUser);
        rc.SetNotificationReadRetentionDays(row.NotificationReadRetentionDays);
        rc.SetCodeRetention(row.BoardCodeRetentionDays, row.PracticeCodeRetentionDays);
        rc.SetDeletedUserRetention(row.DeletedUserRetentionDays);
        rc.SetJourneyEnabled(row.JourneyEnabled);
        rc.SetNotifyKinds(row.NotifyOffKinds, row.NotifyEmailKinds, row.NotifyLockedKinds);
        rc.SetMfaPolicy(row.MfaRequireAdmin, row.MfaRequireOrgAdmin, row.MfaRequireTeacher);
    }

    private static List<AiFallback> Fallbacks(string? json) =>
        AdminAiController.ParseFallbacks(json).Select(f => new AiFallback { ApiKey = f.ApiKey, BaseUrl = f.BaseUrl ?? "", Model = f.Model }).ToList();
}

/// <summary>
/// Re-reads those caches every <c>Realtime:ConfigRefreshSeconds</c>. Default: 15 s when <c>Realtime:Backend=redis</c> (the signal that
/// there is more than one instance), off (0) for a single instance, which already updates its own copy on every save.
/// </summary>
public sealed class RuntimeCacheRefresher(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<RuntimeCacheRefresher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = cfg.GetValue<int?>("Realtime:ConfigRefreshSeconds")
            ?? (string.Equals(cfg["Realtime:Backend"], "redis", StringComparison.OrdinalIgnoreCase) ? 15 : 0);
        if (seconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, seconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await using var scope = scopes.CreateAsyncScope(); await RuntimeCacheLoader.LoadAsync(scope.ServiceProvider, stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "refreshing the settings caches failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
