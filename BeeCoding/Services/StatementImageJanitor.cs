using BeeCoding.Controllers;
using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Deletes statement pictures nobody refers to any more: uploaded, then the author removed the line or never saved the
/// problem. A picture counts as used while any problem statement still names it — board problems and bank problems,
/// including soft-deleted ones, since those can be restored. A picture is deleted <c>Images:OrphanRetentionDays</c>
/// (default 3, 0 = never delete) after the sweep that first found it unused — not after the upload — so removing a picture
/// from a statement, or uploading one for a problem not saved yet, always leaves time to change your mind.
/// </summary>
public sealed class StatementImageJanitor(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<StatementImageJanitor> log) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int Batch = 400;   // SQLite caps the number of parameters in one statement

    private readonly TimeSpan _grace = TimeSpan.FromDays(Math.Max(0, cfg.GetValue("Images:OrphanRetentionDays", 3)));
    private readonly bool _enabled = cfg.GetValue("Images:OrphanRetentionDays", 3) > 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "statement image sweep failed"); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass; returns how many pictures were deleted.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var cutoff = now - _grace;

        var pictures = await db.StatementImages.Select(i => new { i.Id, i.UnusedSince }).ToListAsync(ct);
        if (pictures.Count == 0) return 0;

        // Every id named by any statement. Only statements that mention the path at all are read.
        var used = new HashSet<string>();
        const string marker = "%/api/images/%";
        foreach (var md in await db.BankProblems.IgnoreQueryFilters().Where(p => EF.Functions.Like(p.StatementMarkdown, marker)).Select(p => p.StatementMarkdown).ToListAsync(ct))
            used.UnionWith(ImagesController.RefsIn(md));
        foreach (var md in await db.Problems.IgnoreQueryFilters().Where(p => EF.Functions.Like(p.StatementMarkdown, marker)).Select(p => p.StatementMarkdown).ToListAsync(ct))
            used.UnionWith(ImagesController.RefsIn(md));

        // In use again: forget that it was ever unused. Newly unused: start the clock now (not at upload time),
        // so taking a picture out of a statement always leaves the full grace period.
        foreach (var chunk in pictures.Where(p => used.Contains(p.Id) && p.UnusedSince != null).Select(p => p.Id).Chunk(Batch))
        {
            var ids = chunk.ToList();
            await db.StatementImages.Where(i => ids.Contains(i.Id)).ExecuteUpdateAsync(u => u.SetProperty(i => i.UnusedSince, (DateTime?)null), ct);
        }
        foreach (var chunk in pictures.Where(p => !used.Contains(p.Id) && p.UnusedSince == null).Select(p => p.Id).Chunk(Batch))
        {
            var ids = chunk.ToList();
            await db.StatementImages.Where(i => ids.Contains(i.Id)).ExecuteUpdateAsync(u => u.SetProperty(i => i.UnusedSince, (DateTime?)now), ct);
        }

        int removed = 0;
        foreach (var chunk in pictures.Where(p => !used.Contains(p.Id) && p.UnusedSince != null && p.UnusedSince < cutoff).Select(p => p.Id).Chunk(Batch))
        {
            var ids = chunk.ToList();
            removed += await db.StatementImages.Where(i => ids.Contains(i.Id) && i.UnusedSince < cutoff).ExecuteDeleteAsync(ct);
        }
        if (removed > 0) log.LogInformation("Statement image janitor removed {Count} picture(s) unused for over {Days} day(s)", removed, _grace.TotalDays);
        return removed;
    }
}
