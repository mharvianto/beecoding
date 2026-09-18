using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lti;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Web-tier side of the judge split: takes verdicts the judge computed (over an in-process
/// channel, or a Redis pub/sub in the split deployment), writes them to the submission row,
/// and fires the SignalR notifications + XP award. The judge process itself never touches
/// the database or SignalR.
/// </summary>
public sealed class GradeResultConsumer(
    IGradeResultStream stream, IServiceScopeFactory scopes, ILogger<GradeResultConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct) =>
        await Task.WhenAll(ConsumeResultsAsync(ct), ConsumeProgressAsync(ct));

    private async Task ConsumeResultsAsync(CancellationToken ct)
    {
        await foreach (var r in stream.ReadResultsAsync(ct))
        {
            try
            {
                if (r.Kind == "practice") await ApplyPracticeAsync(r, ct);
                else await ApplyBoardAsync(r, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "failed to apply grade result for {Kind} #{Id}", r.Kind, r.SubmissionId);
            }
        }
    }

    private async Task ConsumeProgressAsync(CancellationToken ct)
    {
        await foreach (var p in stream.ReadProgressAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<IBoardNotifier>();

                var userId = p.Kind == "practice"
                    ? await db.BankSubmissions.Where(s => s.Id == p.SubmissionId).Select(s => (int?)s.UserId).FirstOrDefaultAsync(ct)
                    : await db.Submissions.Where(s => s.Id == p.SubmissionId).Select(s => (int?)s.UserId).FirstOrDefaultAsync(ct);
                if (userId is int uid)
                    await notifier.SubmissionProgressAsync(uid, p.Kind, p.SubmissionId, p.Current, p.Total);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "failed to relay grade progress for {Kind} #{Id}", p.Kind, p.SubmissionId);
            }
        }
    }

    private static Verdict Parse(string s) =>
        Enum.TryParse<Verdict>(s, out var v) ? v : Verdict.RuntimeError;

    private async Task ApplyBoardAsync(GradeResult r, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var notifier = sp.GetRequiredService<IBoardNotifier>();
        var progress = sp.GetRequiredService<ProgressService>();

        var sub = await db.Submissions.Include(s => s.Problem).Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == r.SubmissionId, ct);
        if (sub is null || sub.Problem is null) return;

        var problem = sub.Problem;
        var authorName = sub.User?.DisplayName ?? "student";

        sub.Status = SubmissionStatus.Done;
        sub.Verdict = Parse(r.Verdict);
        sub.Score = r.Score;
        sub.RuntimeMs = r.RuntimeMs;
        sub.MemoryKb = r.MemoryKb;
        sub.CompilerOutput = r.CompilerOutput;
        sub.FailedTest = r.FailedTest;
        sub.JudgedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        int xp = 0;
        if (sub.Verdict == Verdict.Accepted && sub.Score >= 1.0)
        {
            xp = await progress.AwardSolveAsync(sub.UserId, ProgressService.KeyForBoardProblem(problem), problem.Level, ct);
            // Board solves don't bump the practice streak (see User.CurrentStreak), but they
            // do count toward the combined "most solved in a day" record.
            await progress.UpdateMaxSolvedInADayAsync(sub.UserId, sub.LocalDay ?? DateOnly.FromDateTime(DateTime.UtcNow), ct);
        }
        sub.XpAwarded = xp;
        await db.SaveChangesAsync(ct);

        await notifier.ProgressChangedAsync(problem.BoardId, problem.Id, sub.UserId);
        await notifier.WallChangedAsync(problem.BoardId);
        await notifier.SubmissionResultAsync(sub.UserId,
            Mapping.ToDto(sub, sub.UserId, canSeeCode: true, authorName));
        if (xp > 0)
            await notifier.ProgressBumpedAsync(sub.UserId, await progress.GetAsync(sub.UserId, ct: ct));
        // Sync on every judged submission, not just a newly-solved problem — the LMS
        // gradebook then reflects current progress (and "InProgress" status) even before
        // anything is fully solved. Safe to call unconditionally: the sync itself
        // recomputes solved-count fresh from the DB, so a failing/partial submission on
        // this or any other problem never regresses an already-reported score.
        await sp.GetRequiredService<LtiGradeSyncService>().SyncBoardAsync(problem.BoardId, sub.UserId, ct);
    }

    private async Task ApplyPracticeAsync(GradeResult r, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var notifier = sp.GetRequiredService<IBoardNotifier>();
        var progress = sp.GetRequiredService<ProgressService>();

        var sub = await db.BankSubmissions.Include(s => s.BankProblem)
            .FirstOrDefaultAsync(s => s.Id == r.SubmissionId, ct);
        if (sub is null || sub.BankProblem is null) return;

        var problem = sub.BankProblem;

        sub.Status = SubmissionStatus.Done;
        sub.Verdict = Parse(r.Verdict);
        sub.Score = r.Score;
        sub.RuntimeMs = r.RuntimeMs;
        sub.MemoryKb = r.MemoryKb;
        sub.CompilerOutput = r.CompilerOutput;
        sub.JudgedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        int xp = 0;
        bool solvedNow = sub.Verdict == Verdict.Accepted && sub.Score >= 1.0;
        if (solvedNow)
        {
            xp = await progress.AwardSolveAsync(sub.UserId, ProgressService.BankKey(problem.Id), problem.Level, ct);
            var localDay = sub.LocalDay ?? DateOnly.FromDateTime(DateTime.UtcNow);
            await progress.UpdateStreakAsync(sub.UserId, localDay, ct);
            await progress.UpdateMaxSolvedInADayAsync(sub.UserId, localDay, ct);
        }
        sub.XpAwarded = xp;
        await db.SaveChangesAsync(ct);

        await notifier.PracticeResultAsync(sub.UserId, Mapping.ToDto(sub));
        // Push on every solve (not just a fresh xp>0 one) so a repeat Accepted still refreshes
        // the client's live streak count — the frontend's own xpAwarded>0 check still gates
        // the "first solve" celebration separately.
        if (solvedNow)
            await notifier.ProgressBumpedAsync(sub.UserId, await progress.GetAsync(sub.UserId, ct: ct));
        // Sync on every judged submission (not just a full solve) so partial credit shows
        // up in the LMS gradebook as the student improves — safe unconditionally, since
        // the sync takes the MAX score across all of this student's submissions, so a
        // worse later attempt never regresses an already-reported score.
        await sp.GetRequiredService<LtiGradeSyncService>().SyncPracticeAsync(problem.Id, sub.UserId, ct);
    }
}
