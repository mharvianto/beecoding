using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Daily: deletes private BoardOnly bank rows that no board problem points at any more (the problem or its board was purged).
/// A soft-deleted problem still counts as pointing at its row, since it can be restored.
/// </summary>
public sealed class BoardOnlyJanitor(IServiceScopeFactory scopes, ILogger<BoardOnlyJanitor> log) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "board-only bank row sweep failed"); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass; returns how many rows were deleted.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BankProblems.IgnoreQueryFilters()
            .Where(b => b.BoardOnly && !db.Problems.IgnoreQueryFilters().Any(p => p.BankProblemId == b.Id))
            .ExecuteDeleteAsync(ct);
    }
}
