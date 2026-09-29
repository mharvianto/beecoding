using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Deletes notifications older than the retention period (30 days by default), read or not. The bell only ever
/// lists the latest 30, so anything this old is invisible anyway — leaving it would just let
/// the table (and an unread badge nobody can clear) grow forever. Safe to run on every
/// instance: the delete is idempotent.
/// </summary>
public sealed class NotificationJanitor(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<NotificationJanitor> log) : BackgroundService
{
    /// <summary>Config <c>Notifications:RetentionDays</c> (default 30).</summary>
    private readonly TimeSpan _maxAge = TimeSpan.FromDays(Math.Max(0, cfg.GetValue("Notifications:RetentionDays", 30)));
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

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow - _maxAge;
            var removed = await db.Notifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
            if (removed > 0) _log.LogInformation("Notification janitor removed {Count} notification(s) older than {Days} days", removed, _maxAge.TotalDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "notification sweep failed");
        }
    }
}
