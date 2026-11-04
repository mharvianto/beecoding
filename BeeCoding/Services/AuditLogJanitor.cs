using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Deletes audit-log rows older than <c>Audit:RetentionDays</c> (default 365, 0 = keep forever). Without it the table
/// grows for ever and the Audit tab's search scans all of it.
/// </summary>
public sealed class AuditLogJanitor(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<AuditLogJanitor> log) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly int _days = Math.Max(0, cfg.GetValue("Audit:RetentionDays", 365));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_days == 0) return;
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "audit log sweep failed"); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass; returns how many rows were deleted.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (_days == 0) return 0;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-_days);
        return await db.AuditLogEntries.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
