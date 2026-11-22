using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using BeeCoding.Services.Realtime;

namespace BeeCoding.Services;

/// <summary>
/// Privacy: blanks the source code (and compiler output / failed-test detail, which echo it) of submissions older than the
/// retention set in Admin → System (board and practice separately, 0 = keep). The row stays: verdict, score, times, XP and grades
/// never depend on the code. Only finished submissions are touched, so a queued one that JudgeRecovery re-sends keeps its code.
/// Idempotent and safe on every instance.
/// </summary>
public sealed class SubmissionCodeJanitor(IServiceScopeFactory scopes, PlatformRuntimeConfig runtime, ILogger<SubmissionCodeJanitor> log, JobLock? locks = null) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (locks.ShouldRun("submission-code", Interval)) await SweepAsync(stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            if (runtime.BoardCodeRetentionDays > 0)
            {
                var cutoff = now.AddDays(-runtime.BoardCodeRetentionDays);
                var n = await db.Submissions.Where(s => s.CodePurgedAt == null && s.Status == SubmissionStatus.Done && s.CreatedAt < cutoff)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Code, "").SetProperty(s => s.CompilerOutput, "").SetProperty(s => s.FailedTest, (string?)null).SetProperty(s => s.CodePurgedAt, now), ct);
                if (n > 0) log.LogInformation("Removed the code of {Count} board submission(s) older than {Days} days", n, runtime.BoardCodeRetentionDays);
            }
            if (runtime.PracticeCodeRetentionDays > 0)
            {
                var cutoff = now.AddDays(-runtime.PracticeCodeRetentionDays);
                var n = await db.BankSubmissions.Where(s => s.CodePurgedAt == null && s.Status == SubmissionStatus.Done && s.CreatedAt < cutoff)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Code, "").SetProperty(s => s.CompilerOutput, "").SetProperty(s => s.FailedTest, (string?)null).SetProperty(s => s.CodePurgedAt, now), ct);
                if (n > 0) log.LogInformation("Removed the code of {Count} practice submission(s) older than {Days} days", n, runtime.PracticeCodeRetentionDays);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogWarning(ex, "Submission code sweep failed"); }
    }
}
