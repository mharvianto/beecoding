using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeeCoding.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BeeCoding.Services.Judge;

/// <summary>
/// Broker-backed queue so the judge can run as a separate deployment.
///  • jobs          — a Redis list, RPUSH by the web tier / LPOP by the judge worker
///  • run reply     — pub/sub channel {prefix}run-results, correlated by id
///  • grade results — pub/sub channel {prefix}grade-results, consumed by the web tier
///
/// Caveat: LPOP is at-most-once — a job popped by a judge that then crashes is lost. For
/// production use Redis Streams + consumer groups. Grade jobs must stay idempotent
/// (re-grading overwrites the verdict, so this is safe).
/// </summary>
public sealed class RedisJudgeQueue : IJudgeQueue, IJudgeJobSource, IGradeResultStream, IAsyncDisposable
{
    private readonly IDatabase _db;
    private readonly ISubscriber _sub;
    private readonly JudgeQueueOptions _o;
    private readonly ILogger<RedisJudgeQueue> _log;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunResultDto>> _pendingRuns = new();
    private readonly Channel<GradeResult> _grades = Channel.CreateUnbounded<GradeResult>();
    private readonly RedisChannel _runResults;
    private readonly RedisChannel _gradeResults;
    private readonly string _jobsKey;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RedisJudgeQueue(IConnectionMultiplexer mux, IOptions<JudgeOptions> judgeOptions, ILogger<RedisJudgeQueue> log)
    {
        _o = judgeOptions.Value.Queue;
        _log = log;
        _db = mux.GetDatabase();
        _sub = mux.GetSubscriber();
        _jobsKey = _o.KeyPrefix + "jobs";
        _runResults = RedisChannel.Literal(_o.KeyPrefix + "run-results");
        _gradeResults = RedisChannel.Literal(_o.KeyPrefix + "grade-results");

        _sub.Subscribe(_runResults, (_, msg) =>
        {
            try
            {
                var n = JsonNode.Parse((string)msg!)!;
                var id = (string)n["id"]!;
                var result = n["result"].Deserialize<RunResultDto>(Json);
                if (result is not null && _pendingRuns.TryGetValue(id, out var tcs)) tcs.TrySetResult(result);
            }
            catch (Exception ex) { _log.LogWarning(ex, "bad judge run-result message"); }
        });

        _sub.Subscribe(_gradeResults, (_, msg) =>
        {
            try
            {
                var r = JsonSerializer.Deserialize<GradeResult>((string)msg!, Json);
                if (r is not null) _grades.Writer.TryWrite(r);
            }
            catch (Exception ex) { _log.LogWarning(ex, "bad judge grade-result message"); }
        });
    }

    /// <summary>Jobs waiting to be popped by a judge worker — for the admin system-status
    /// panel. Approximate: at-most-once LPOP means a job a worker is mid-processing (or
    /// lost to a crash) isn't reflected here.</summary>
    public Task<long> PendingJobCountAsync() => _db.ListLengthAsync(_jobsKey);

    // ---- producer (web) ---------------------------------------------------
    public async ValueTask EnqueueGradeAsync(GradeJob job, CancellationToken ct = default) =>
        await _db.ListRightPushAsync(_jobsKey, Encode(job));

    public async Task<RunResultDto> EnqueueRunAsync(
        string language, string code, string stdin, int timeLimitMs, int memoryLimitKb,
        string? inputFileName = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<RunResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRuns[id] = tcs;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _o.RunReplyTimeoutSeconds)));
        try
        {
            await _db.ListRightPushAsync(_jobsKey,
                Encode(new RunJob(language, code, stdin, timeLimitMs, memoryLimitKb, id, inputFileName)));
            return await tcs.Task.WaitAsync(timeout.Token);
        }
        finally { _pendingRuns.TryRemove(id, out _); }
    }

    // ---- consumer (judge worker) ---------------------------------------------------
    public async IAsyncEnumerable<JudgeJob> ReadJobsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            RedisValue v;
            try { v = await _db.ListLeftPopAsync(_jobsKey); }
            catch (Exception ex) { _log.LogWarning(ex, "judge queue pop failed"); await Delay(ct); continue; }

            if (v.IsNullOrEmpty) { await Delay(ct); continue; }

            JudgeJob? job = null;
            try { job = Decode((string)v!); }
            catch (Exception ex) { _log.LogWarning(ex, "undecodable judge job dropped: {Raw}", v); }
            if (job is not null) yield return job;
        }
    }

    public ValueTask ReportRunResultAsync(RunJob job, RunResultDto result)
    {
        var payload = new JsonObject { ["id"] = job.CorrelationId, ["result"] = JsonSerializer.SerializeToNode(result, Json) };
        return new ValueTask(_sub.PublishAsync(_runResults, payload.ToJsonString()));
    }

    public ValueTask ReportGradeResultAsync(GradeResult result) =>
        new(_sub.PublishAsync(_gradeResults, JsonSerializer.Serialize(result, Json)));

    // ---- grade-result side (web) ---------------------------------------------------
    public IAsyncEnumerable<GradeResult> ReadResultsAsync(CancellationToken ct) => _grades.Reader.ReadAllAsync(ct);

    private async Task Delay(CancellationToken ct)
    {
        try { await Task.Delay(Math.Max(50, _o.PollMs), ct); } catch (OperationCanceledException) { }
    }

    private static string Encode(JudgeJob job) => job switch
    {
        RunJob r => new JsonObject { ["kind"] = "run", ["data"] = JsonSerializer.SerializeToNode(r, Json) }.ToJsonString(),
        GradeJob g => new JsonObject { ["kind"] = "grade", ["data"] = JsonSerializer.SerializeToNode(g, Json) }.ToJsonString(),
        _ => throw new NotSupportedException(job.GetType().Name),
    };

    private static JudgeJob Decode(string raw)
    {
        var n = JsonNode.Parse(raw)!;
        return (string)n["kind"]! switch
        {
            "run" => n["data"].Deserialize<RunJob>(Json)!,
            "grade" => n["data"].Deserialize<GradeJob>(Json)!,
            var k => throw new NotSupportedException(k),
        };
    }

    public async ValueTask DisposeAsync()
    {
        try { await _sub.UnsubscribeAsync(_runResults); } catch { /* ignore */ }
        try { await _sub.UnsubscribeAsync(_gradeResults); } catch { /* ignore */ }
    }
}
