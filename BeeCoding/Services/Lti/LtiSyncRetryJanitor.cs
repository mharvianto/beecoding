using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using BeeCoding.Services.Realtime;

namespace BeeCoding.Services.Lti;

/// <summary>Every 2 minutes: sends again the grades the LMS did not accept (<c>LtiSyncRetry</c>, set by
/// <see cref="LtiGradeSyncService.TrackResultAsync"/>), at most 50 per pass. Waits grow 1, 5, 15, 60, 180, 720 minutes, then the
/// row is dropped. Survives a restart, which a fire-and-forget send does not.</summary>
public sealed class LtiSyncRetryJanitor(IServiceScopeFactory scopes, ILogger<LtiSyncRetryJanitor> log, JobLock? locks = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (locks.ShouldRun("lti-retry", TimeSpan.FromMinutes(2))) await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "LTI retry sweep failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass; returns how many sends were tried.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        List<(string Kind, int Target, int User)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            due = (await db.LtiSyncRetries.Where(x => x.NextAttemptAt <= now).OrderBy(x => x.NextAttemptAt).Take(50)
                .Select(x => new { x.Kind, x.TargetId, x.UserId }).ToListAsync(ct)).Select(x => (x.Kind, x.TargetId, x.UserId)).ToList();
        }
        foreach (var (kind, target, user) in due)
        {
            await using var scope = scopes.CreateAsyncScope();   // a scope each: a failed save must not poison the next one
            var sync = scope.ServiceProvider.GetRequiredService<LtiGradeSyncService>();
            if (kind == "practice") await sync.SyncPracticeAsync(target, user, ct);
            else await sync.SyncBoardAsync(target, user, ct);
        }
        return due.Count;
    }
}
