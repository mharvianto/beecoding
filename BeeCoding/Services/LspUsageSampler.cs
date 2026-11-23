using BeeCoding.Data;
using BeeCoding.Services.Lsp;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Once a minute records how many students have a clangd editor attached on THIS instance (Admin → System → "IntelliSense use"),
/// so a teacher can see the peak of a class afterwards; the live "x / y attached" number has no history. A row is written only
/// while someone is attached, plus the minute it falls back to zero, so an idle server stores nothing. Rows older than 30 days go.
/// Per instance on purpose (no <c>JobLock</c>): each web instance only knows its own sessions, the chart adds the instances up.
/// </summary>
public sealed class LspUsageSampler(IServiceScopeFactory scopes, LspBridge bridge, SharedClangd shared, ILogger<LspUsageSampler> log) : BackgroundService
{
    public const int KeepDays = 30;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private bool _lastWasBusy;
    private int _ticks;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { break; }
            try { await SampleAsync(DateTime.UtcNow, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "lsp usage sample failed"); }
        }
    }

    /// <summary>One tick; returns true when a row was written.</summary>
    public async Task<bool> SampleAsync(DateTime now, CancellationToken ct)
    {
        var students = bridge.AttachedStudents;
        var busy = students > 0;
        var write = busy || _lastWasBusy;
        _lastWasBusy = busy;
        var prune = ++_ticks % 60 == 0;   // hourly
        if (!write && !prune) return false;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (write)
        {
            db.LspUsageSamples.Add(new LspUsageSample { At = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc), Host = Environment.MachineName, Students = students, Processes = shared.Processes });
            await db.SaveChangesAsync(ct);
        }
        if (prune) await db.LspUsageSamples.Where(x => x.At < now.AddDays(-KeepDays)).ExecuteDeleteAsync(ct);
        return write;
    }

    /// <summary>One point per minute for a day, coarser (the busiest minute of each step) for longer windows; several instances are added up per minute.</summary>
    public static async Task<LspUsageDto> ReportAsync(AppDbContext db, int hours)
    {
        hours = Math.Clamp(hours, 1, 24 * KeepDays);
        var since = DateTime.UtcNow.AddHours(-hours);
        var perMinute = (await db.LspUsageSamples.Where(x => x.At >= since)
                .Select(x => new { x.At, x.Students, x.Processes }).ToListAsync())
            .GroupBy(x => x.At).OrderBy(g => g.Key)
            .Select(g => (At: g.Key, Students: g.Sum(x => x.Students), Processes: g.Sum(x => x.Processes))).ToList();

        var step = hours <= 24 ? 1 : hours <= 72 ? 5 : 15;
        var ticks = TimeSpan.FromMinutes(step).Ticks;
        var points = perMinute
            .GroupBy(m => m.At.Ticks / ticks)
            .Select(g => g.OrderByDescending(m => m.Students).First() with { At = new DateTime(g.Key * ticks, DateTimeKind.Utc) })
            .Select(m => new LspUsagePointDto(m.At.ToString("o"), m.Students, m.Processes)).ToList();
        if (perMinute.Count == 0) return new LspUsageDto(hours, step, 0, null, points);
        var peak = perMinute.MaxBy(m => m.Students);
        return new LspUsageDto(hours, step, peak.Students, peak.At.ToString("o"), points);
    }
}
