using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using BeeCoding.Services.Realtime;

namespace BeeCoding.Services;

/// <summary>Daily: drops saved admin AI check/triage results (<c>AiReviewResult</c>) older than 180 days.</summary>
public sealed class AiReviewJanitor(IServiceScopeFactory scopes, ILogger<AiReviewJanitor> log, JobLock? locks = null) : BackgroundService
{
    public const int RetentionDays = 180;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (locks.ShouldRun("ai-review", TimeSpan.FromHours(24))) await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "AI review sweep failed"); }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        return await db.AiReviewResults.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
