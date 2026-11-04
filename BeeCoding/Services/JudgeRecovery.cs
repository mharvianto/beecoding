using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// The in-process judge queue lives in memory, so a restart/deploy drops whatever was waiting and
/// those submissions would sit as Queued forever. Once at startup, re-enqueue the recent ones.
/// Not registered for the Redis queue — its jobs survive a web restart. Re-grading is safe: XP is
/// awarded once per problem (SolveRecord), so a repeat verdict changes nothing twice.
/// Nothing else ever sets <see cref="SubmissionStatus.Running"/>, so here it means "already retried once":
/// a row still Running at the next start was in flight when the process died a second time. It is failed
/// instead of queued again, so a submission that takes the process down is not re-run on every restart.
/// </summary>
public sealed class JudgeRecovery(IServiceScopeFactory scopes, IJudgeQueue queue, ILogger<JudgeRecovery> log) : BackgroundService
{
    // shortcut: only the last day — older ones are abandoned; widen if a long outage needs it.
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private const string GaveUp = "The judge was interrupted twice while grading this submission. Please submit it again.";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateTime.UtcNow - MaxAge;
            var jobs = new List<GradeJob>();

            var board = await db.Submissions.Include(s => s.Problem).ThenInclude(p => p!.TestCases)
                .Where(s => s.Status != SubmissionStatus.Done && s.CreatedAt > since && s.Problem != null)
                .OrderBy(s => s.Id).ToListAsync(ct);
            foreach (var s in board)
            {
                if (s.Status == SubmissionStatus.Running) { s.Status = SubmissionStatus.Done; s.Verdict = Verdict.RuntimeError; s.CompilerOutput = GaveUp; s.JudgedAt = DateTime.UtcNow; continue; }
                s.Status = SubmissionStatus.Running;
                var p = s.Problem!;
                jobs.Add(new GradeJob("board", s.Id, s.Language ?? Languages.Default(p.AllowedLanguages), s.Code,
                    p.TimeLimitMs, p.MemoryLimitKb, p.BannedHeaders, p.BannedSymbols, Tests(p.TestCases), p.InputFileName));
            }

            var practice = await db.BankSubmissions.Include(s => s.BankProblem).ThenInclude(p => p!.TestCases)
                .Where(s => s.Status != SubmissionStatus.Done && s.CreatedAt > since && s.BankProblem != null)
                .OrderBy(s => s.Id).ToListAsync(ct);
            foreach (var s in practice)
            {
                if (s.Status == SubmissionStatus.Running) { s.Status = SubmissionStatus.Done; s.Verdict = Verdict.RuntimeError; s.CompilerOutput = GaveUp; s.JudgedAt = DateTime.UtcNow; continue; }
                s.Status = SubmissionStatus.Running;
                var p = s.BankProblem!;
                jobs.Add(new GradeJob("practice", s.Id, s.Language ?? Languages.Default(p.AllowedLanguages), s.Code,
                    p.TimeLimitMs, p.MemoryLimitKb, p.BannedHeaders, p.BannedSymbols, Tests(p.TestCases), p.InputFileName));
            }

            await db.SaveChangesAsync(ct);   // remember the attempt BEFORE grading: a crash mid-grade must show up as Running next start
            foreach (var j in jobs) await queue.EnqueueGradeAsync(j, ct);
            if (board.Count + practice.Count > 0)
                log.LogWarning("Judge recovery: re-queued {Queued}, gave up on {GaveUp} left unfinished by earlier shutdowns",
                    jobs.Count, board.Count + practice.Count - jobs.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogError(ex, "Judge recovery failed"); }
    }

    private static List<TestSpec> Tests(IEnumerable<TestCase> t) =>
        t.OrderBy(x => x.Position).ThenBy(x => x.Id).Select(x => new TestSpec(x.Stdin, x.ExpectedStdout, x.Points, x.IsSample)).ToList();

    private static List<TestSpec> Tests(IEnumerable<BankTestCase> t) =>
        t.OrderBy(x => x.Position).ThenBy(x => x.Id).Select(x => new TestSpec(x.Stdin, x.ExpectedStdout, x.Points, x.IsSample)).ToList();
}
