using BeeCoding.Services.Judge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The Redis broker keeps what a consumer took but never finished. Needs a Redis 6.2+ (<c>BEECODING_TEST_REDIS=host:port</c>).</summary>
public class RedisJudgeQueueTests
{
    private static readonly string? Redis = Environment.GetEnvironmentVariable("BEECODING_TEST_REDIS") is { Length: > 0 } s ? s : null;
    private static readonly Lazy<IConnectionMultiplexer> Mux = new(() => ConnectionMultiplexer.Connect(Redis!));

    /// <summary>Several queue objects over one Redis and one key prefix: each is a node (a web replica, or a judge worker).</summary>
    private static Func<RedisJudgeQueue> Nodes(out string prefix)
    {
        var p = prefix = "t" + Guid.NewGuid().ToString("N")[..8] + ":";
        var opt = Options.Create(new JudgeOptions { Queue = { KeyPrefix = p, ReclaimAfterSeconds = 2, PollMs = 50 } });
        return () => new RedisJudgeQueue(Mux.Value, opt, NullLogger<RedisJudgeQueue>.Instance);
    }

    private static GradeResult Result(int id) => new("board", id, "Accepted", 1, 1, 1, "");
    private static GradeJob Job(int id) => new("board", id, "cpp", "int main(){}", 1000, 1000, null, null, [new TestSpec("", "", 1)]);

    private static async Task<T?> FirstAsync<T>(IAsyncEnumerable<T> src, TimeSpan within) where T : class
    {
        using var cts = new CancellationTokenSource(within);
        try { await foreach (var x in src.WithCancellation(cts.Token)) return x; } catch (OperationCanceledException) { }
        return null;
    }

    [Fact]
    public async Task A_verdict_goes_to_one_node_and_is_handed_on_if_that_node_dies_before_finishing_it()
    {
        if (Redis is null) return;
        var node = Nodes(out var prefix);
        await node().ReportGradeResultAsync(Result(7));

        // node A takes it and "crashes": it never comes back for the next one
        var a = node().ReadResultsAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await a.MoveNextAsync());
        Assert.Equal(7, a.Current.SubmissionId);
        Assert.Null(await FirstAsync(node().ReadResultsAsync(CancellationToken.None), TimeSpan.FromMilliseconds(300)));   // not yet: A may still be applying it

        var again = await FirstAsync(node().ReadResultsAsync(CancellationToken.None), TimeSpan.FromSeconds(10));          // B takes it over
        Assert.Equal(7, again!.SubmissionId);

        // a node that finishes (asks for the next one) acknowledges it: nothing is left to take over
        var c = node().ReadResultsAsync(CancellationToken.None).GetAsyncEnumerator();
        await node().ReportGradeResultAsync(Result(8));
        Assert.True(await c.MoveNextAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try { await c.MoveNextAsync().AsTask().WaitAsync(cts.Token); } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
        Assert.Equal(1, await Mux.Value.GetDatabase().StreamLengthAsync(prefix + "grade-results-s"));   // 7 is still pending (nobody finished it), 8 was acknowledged and deleted
    }

    [Fact]
    public async Task Every_verdict_reaches_exactly_one_of_two_nodes()
    {
        if (Redis is null) return;
        var node = Nodes(out _);
        var producer = node();
        for (var i = 1; i <= 20; i++) await producer.ReportGradeResultAsync(Result(i));

        var got = new System.Collections.Concurrent.ConcurrentBag<int>();
        // Read until all 20 have arrived (15 s ceiling): a fixed 2 s window failed on slow CI runners with only 2 read.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task Consume() { try { await foreach (var r in node().ReadResultsAsync(cts.Token)) { got.Add(r.SubmissionId); if (got.Count >= 20) cts.Cancel(); } } catch (OperationCanceledException) { } }
        await Task.WhenAll(Consume(), Consume());
        Assert.Equal(Enumerable.Range(1, 20), got.OrderBy(x => x));
    }

    [Fact]
    public async Task A_job_stays_until_the_worker_acknowledges_it_and_a_stale_ad_hoc_run_is_dropped()
    {
        if (Redis is null) return;
        var node = Nodes(out _);
        var web = node();
        await web.EnqueueGradeAsync(Job(5));
        Assert.Equal(1, await web.PendingJobCountAsync());

        var w1 = node();
        var job = await FirstAsync(w1.ReadJobsAsync(CancellationToken.None), TimeSpan.FromSeconds(5));
        Assert.Equal(5, ((GradeJob)job!).SubmissionId);
        Assert.Equal(1, await web.PendingJobCountAsync());                       // taken but not finished: still counted

        var w2 = node();
        var taken = await FirstAsync(w2.ReadJobsAsync(CancellationToken.None), TimeSpan.FromSeconds(10));   // worker 1 "died": worker 2 takes it over
        Assert.Equal(5, ((GradeJob)taken!).SubmissionId);
        await w2.AckAsync(taken);
        Assert.Equal(0, await web.PendingJobCountAsync());

        // an ad-hoc Run that nobody finished within the reclaim time is dropped: whoever asked has stopped waiting
        using var asker = new CancellationTokenSource();
        var run = web.EnqueueRunAsync("cpp", "int main(){}", "", 1000, 1000, null, asker.Token);
        var first = await FirstAsync(node().ReadJobsAsync(CancellationToken.None), TimeSpan.FromSeconds(5));
        Assert.IsType<RunJob>(first);                                            // taken, then abandoned
        Assert.Null(await FirstAsync(node().ReadJobsAsync(CancellationToken.None), TimeSpan.FromSeconds(4)));
        Assert.Equal(0, await web.PendingJobCountAsync());
        asker.Cancel();
        try { await run; } catch (OperationCanceledException) { }
    }
}
