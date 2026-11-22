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
///  • jobs          — a Redis Stream {prefix}jobs-s read by the consumer group "judges": XADD by the web tier, XREADGROUP by the workers
///  • run reply     — pub/sub channel {prefix}run-results, correlated by id
///  • grade results — a Redis Stream {prefix}grade-results-s, group "web": XADD by the judge, XREADGROUP by the web tier. A stream, not
///    pub/sub: a verdict published while the web tier restarts would be lost (the submission stays Queued for good), and every web
///    replica would receive and apply the same verdict; in a group exactly one replica gets each one.
///
/// At-least-once: an entry stays pending until it is acknowledged (and then deleted) — a job when the worker finished it
/// (<see cref="AckAsync"/>), a verdict when the web tier asked for the next one — so a judge or web node that crashes mid-way does not
/// lose it: after <see cref="JudgeQueueOptions.ReclaimAfterSeconds"/> another consumer takes it over. So an entry can run twice (a slow
/// one is taken over while still running): grading overwrites the verdict, XP is awarded once per problem, so that is safe. A stale RunJob
/// is dropped instead of re-run, since the person who asked has stopped waiting.
/// </summary>
public sealed class RedisJudgeQueue : IJudgeQueue, IJudgeJobSource, IGradeResultStream, IAsyncDisposable
{
    private readonly IDatabase _db;
    private readonly ISubscriber _sub;
    private readonly JudgeQueueOptions _o;
    private readonly ILogger<RedisJudgeQueue> _log;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunResultDto>> _pendingRuns = new();
    private readonly Channel<GradeProgress> _progress = Channel.CreateUnbounded<GradeProgress>();
    private readonly RedisChannel _runResults;
    private readonly string _gradeResultsKey;
    private readonly RedisChannel _gradeProgress;
    private readonly string _jobsKey;
    private const string JobsGroup = "judges", ResultsGroup = "web";
    private readonly string _consumer = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];
    // which stream entry a job came from, so the worker can acknowledge it when it is done
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<JudgeJob, string> _entryOf = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RedisJudgeQueue(IConnectionMultiplexer mux, IOptions<JudgeOptions> judgeOptions, ILogger<RedisJudgeQueue> log)
    {
        _o = judgeOptions.Value.Queue;
        _log = log;
        _db = mux.GetDatabase();
        _sub = mux.GetSubscriber();
        _jobsKey = _o.KeyPrefix + "jobs-s";
        _runResults = RedisChannel.Literal(_o.KeyPrefix + "run-results");
        _gradeResultsKey = _o.KeyPrefix + "grade-results-s";
        _gradeProgress = RedisChannel.Literal(_o.KeyPrefix + "grade-progress");

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

        _sub.Subscribe(_gradeProgress, (_, msg) =>
        {
            try
            {
                var p = JsonSerializer.Deserialize<GradeProgress>((string)msg!, Json);
                if (p is not null) _progress.Writer.TryWrite(p);
            }
            catch (Exception ex) { _log.LogWarning(ex, "bad judge grade-progress message"); }
        });
    }

    /// <summary>Jobs not finished yet (waiting, plus the ones a worker is running) — for the admin system-status panel.</summary>
    public Task<long> PendingJobCountAsync() => _db.StreamLengthAsync(_jobsKey);

    // ---- producer (web) ---------------------------------------------------
    public async ValueTask EnqueueGradeAsync(GradeJob job, CancellationToken ct = default) =>
        await _db.StreamAddAsync(_jobsKey, "d", Encode(job));

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
            await _db.StreamAddAsync(_jobsKey, "d",
                Encode(new RunJob(language, code, stdin, timeLimitMs, memoryLimitKb, id, inputFileName)));
            return await tcs.Task.WaitAsync(timeout.Token);
        }
        finally { _pendingRuns.TryRemove(id, out _); }
    }

    // ---- consumer (judge worker) ---------------------------------------------------
    public async IAsyncEnumerable<JudgeJob> ReadJobsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureGroupAsync(_jobsKey, JobsGroup, ct);
        while (!ct.IsCancellationRequested)
        {
            (StreamEntry Entry, bool Reclaimed)? next;
            try { next = await NextAsync(_jobsKey, JobsGroup); }
            catch (Exception ex) { _log.LogWarning(ex, "judge queue read failed"); await Delay(ct); continue; }
            if (next is null) { await Delay(ct); continue; }
            var (e, reclaimed) = next.Value;

            JudgeJob? job = null;
            try { job = Decode(e.Values.First(v => v.Name == "d").Value!); }
            catch (Exception ex) { _log.LogWarning(ex, "undecodable judge job dropped"); }
            // a RunJob that waited out the reclaim time has no one left to answer; an undecodable one never will
            if (job is null || (job is RunJob && reclaimed)) { await DoneAsync(_jobsKey, JobsGroup, e.Id); continue; }
            _entryOf.AddOrUpdate(job, (string)e.Id!);
            yield return job;
        }
    }

    /// <summary>The worker is finished with a job (it reported a result, or failed in a way it reported): take it off the stream.</summary>
    public async ValueTask AckAsync(JudgeJob job)
    {
        if (_entryOf.TryGetValue(job, out var id)) await DoneAsync(_jobsKey, JobsGroup, id);
    }

    public ValueTask ReportRunResultAsync(RunJob job, RunResultDto result)
    {
        var payload = new JsonObject { ["id"] = job.CorrelationId, ["result"] = JsonSerializer.SerializeToNode(result, Json) };
        return new ValueTask(_sub.PublishAsync(_runResults, payload.ToJsonString()));
    }

    public async ValueTask ReportGradeResultAsync(GradeResult result) =>
        await _db.StreamAddAsync(_gradeResultsKey, "d", JsonSerializer.Serialize(result, Json));

    public ValueTask ReportGradeProgressAsync(GradeProgress progress) =>
        new(_sub.PublishAsync(_gradeProgress, JsonSerializer.Serialize(progress, Json)));

    // ---- grade-result side (web) ---------------------------------------------------
    public async IAsyncEnumerable<GradeResult> ReadResultsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureGroupAsync(_gradeResultsKey, ResultsGroup, ct);
        while (!ct.IsCancellationRequested)
        {
            (StreamEntry Entry, bool Reclaimed)? next;
            try { next = await NextAsync(_gradeResultsKey, ResultsGroup); }
            catch (Exception ex) { _log.LogWarning(ex, "grade result read failed"); await Delay(ct); continue; }
            if (next is null) { await Delay(ct); continue; }
            var e = next.Value.Entry;

            GradeResult? r = null;
            try { r = JsonSerializer.Deserialize<GradeResult>((string)e.Values.First(v => v.Name == "d").Value!, Json); }
            catch (Exception ex) { _log.LogWarning(ex, "bad judge grade-result message dropped"); }
            if (r is not null) yield return r;   // the caller is back for the next one only after it has applied this one ...
            await DoneAsync(_gradeResultsKey, ResultsGroup, e.Id);   // ... so a crash while applying leaves it pending for another node
        }
    }
    public IAsyncEnumerable<GradeProgress> ReadProgressAsync(CancellationToken ct) => _progress.Reader.ReadAllAsync(ct);

    private async Task EnsureGroupAsync(string key, string group, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _db.StreamCreateConsumerGroupAsync(key, group, "0", createStream: true); return; }
            catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP")) { return; }   // another node made it first
            catch (Exception ex) { _log.LogWarning(ex, "cannot create consumer group {Group}", group); await Delay(ct); }
        }
    }

    // An entry a dead consumer left pending comes first (after it has been idle for ReclaimAfterSeconds), then new ones.
    private async Task<(StreamEntry Entry, bool Reclaimed)?> NextAsync(string key, string group)
    {
        var idle = (long)TimeSpan.FromSeconds(Math.Max(1, _o.ReclaimAfterSeconds)).TotalMilliseconds;
        var claimed = await _db.StreamAutoClaimAsync(key, group, _consumer, idle, "0-0", 1);
        if (claimed.ClaimedEntries.Length > 0) return (claimed.ClaimedEntries[0], true);
        var fresh = await _db.StreamReadGroupAsync(key, group, _consumer, ">", 1);
        return fresh.Length > 0 ? (fresh[0], false) : null;
    }

    private async Task DoneAsync(string key, string group, RedisValue id)
    {
        try { await _db.StreamAcknowledgeAsync(key, group, id); await _db.StreamDeleteAsync(key, [id]); }
        catch (Exception ex) { _log.LogWarning(ex, "could not acknowledge stream entry {Id}", (string?)id); }   // it is taken over later and the verdict overwritten
    }

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
        try { await _sub.UnsubscribeAsync(_gradeProgress); } catch { /* ignore */ }
    }
}
