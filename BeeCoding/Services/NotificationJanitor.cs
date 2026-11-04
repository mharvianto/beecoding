using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Deletes notifications older than the retention period (30 days by default), read or not. The bell only ever
/// lists the latest 30, so anything this old is invisible anyway — leaving it would just let
/// the table (and an unread badge nobody can clear) grow forever. Safe to run on every
/// instance: the delete is idempotent.
/// </summary>
public sealed class NotificationJanitor(IServiceScopeFactory scopes, IConfiguration cfg, PlatformRuntimeConfig runtime, ILogger<NotificationJanitor> log) : BackgroundService
{
    /// <summary>Config <c>Notifications:RetentionDays</c> (default 30).</summary>
    private readonly TimeSpan _maxAge = TimeSpan.FromDays(Math.Max(0, cfg.GetValue("Notifications:RetentionDays", 30)));
    /// <summary>Per user, only the newest this many are kept — a busy board can otherwise pile up hundreds.</summary>
    public const int MaxPerUser = 200;
    private readonly PlatformRuntimeConfig _runtime = runtime;
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<NotificationJanitor> _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepAsync(stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow - _maxAge;
            var removed = await db.Notifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
            if (removed > 0) _log.LogInformation("Notification janitor removed {Count} notification(s) older than {Days} days", removed, _maxAge.TotalDays);

            // Read ones go sooner than unread ones (Admin > System: days after reading, 0 = off).
            if (_runtime.NotificationReadRetentionDays > 0)
            {
                var readCutoff = DateTime.UtcNow.AddDays(-_runtime.NotificationReadRetentionDays);
                var readGone = await db.Notifications.Where(n => n.ReadAt != null && n.ReadAt < readCutoff).ExecuteDeleteAsync(ct);
                if (readGone > 0) _log.LogInformation("Notification janitor removed {Count} read notification(s) older than {Days} days", readGone, _runtime.NotificationReadRetentionDays);
            }

            foreach (var userId in await db.Notifications.GroupBy(n => n.UserId).Where(g => g.Count() > MaxPerUser).Select(g => g.Key).ToListAsync(ct))
            {
                var newest = await db.Notifications.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                    .Take(MaxPerUser).Select(n => n.Id).ToListAsync(ct);
                var trimmed = await db.Notifications.Where(n => n.UserId == userId && !newest.Contains(n.Id)).ExecuteDeleteAsync(ct);
                _log.LogInformation("Notification janitor trimmed {Count} old notification(s) for user {User}", trimmed, userId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "notification sweep failed");
        }
    }
}
