using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// The in-process judge queue lives in memory, so a restart/deploy drops whatever was waiting and
/// those submissions would sit as Queued/Running forever. Once at startup, re-enqueue the recent ones.
/// Not registered for the Redis queue — its jobs survive a web restart. Re-grading is safe: XP is
/// awarded once per problem (SolveRecord), so a repeat verdict changes nothing twice.
/// </summary>
public sealed class JudgeRecovery(IServiceScopeFactory scopes, IJudgeQueue queue, ILogger<JudgeRecovery> log) : BackgroundService
{
    // shortcut: only the last day — older ones are abandoned; widen if a long outage needs it.
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateTime.UtcNow - MaxAge;
            int n = 0;

            var board = await db.Submissions.Include(s => s.Problem).ThenInclude(p => p!.TestCases)
                .Where(s => s.Status != SubmissionStatus.Done && s.CreatedAt > since && s.Problem != null)
                .OrderBy(s => s.Id).ToListAsync(ct);
            foreach (var s in board)
            {
                var p = s.Problem!;
                await queue.EnqueueGradeAsync(new GradeJob("board", s.Id, s.Language ?? Languages.Default(p.AllowedLanguages), s.Code,
                    p.TimeLimitMs, p.MemoryLimitKb, p.BannedHeaders, p.BannedSymbols, Tests(p.TestCases), p.InputFileName), ct);
                n++;
            }

            var practice = await db.BankSubmissions.Include(s => s.BankProblem).ThenInclude(p => p!.TestCases)
                .Where(s => s.Status != SubmissionStatus.Done && s.CreatedAt > since && s.BankProblem != null)
                .OrderBy(s => s.Id).ToListAsync(ct);
            foreach (var s in practice)
            {
                var p = s.BankProblem!;
                await queue.EnqueueGradeAsync(new GradeJob("practice", s.Id, s.Language ?? Languages.Default(p.AllowedLanguages), s.Code,
                    p.TimeLimitMs, p.MemoryLimitKb, p.BannedHeaders, p.BannedSymbols, Tests(p.TestCases), p.InputFileName), ct);
                n++;
            }
            if (n > 0) log.LogWarning("Re-queued {Count} submission(s) left unfinished by the last shutdown", n);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogError(ex, "Judge recovery failed"); }
    }

    private static List<TestSpec> Tests(IEnumerable<TestCase> t) =>
        t.OrderBy(x => x.Position).ThenBy(x => x.Id).Select(x => new TestSpec(x.Stdin, x.ExpectedStdout, x.Points, x.IsSample)).ToList();

    private static List<TestSpec> Tests(IEnumerable<BankTestCase> t) =>
        t.OrderBy(x => x.Position).ThenBy(x => x.Id).Select(x => new TestSpec(x.Stdin, x.ExpectedStdout, x.Points, x.IsSample)).ToList();
}
