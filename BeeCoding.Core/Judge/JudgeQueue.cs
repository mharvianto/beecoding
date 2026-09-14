using System.Collections.Concurrent;
using System.Threading.Channels;
using BeeCoding.Models;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Judge;

public abstract record JudgeJob;

/// <summary>
/// A submission to grade. Carries EVERYTHING the judge needs — code, limits, the full test
/// set — so the judge process never touches the database. <see cref="Kind"/> is
/// "board" or "practice".
/// </summary>
public sealed record GradeJob(
    string Kind,
    int SubmissionId,
    string Language,
    string Code,
    int TimeLimitMs,
    int MemoryLimitKb,
    string? BannedHeaders,
    string? BannedSymbols,
    IReadOnlyList<TestSpec> Tests,
    string? InputFileName = null) : JudgeJob;

public sealed record TestSpec(string Stdin, string Expected, int Points, bool IsSample = false);

/// <summary>The judge's verdict for a <see cref="GradeJob"/> — the ONLY thing it sends back.
/// <see cref="FailedTest"/> is a formatted "test N of M / input / expected / your output"
/// block for the first test that didn't pass (null on Accepted or CompileError, where
/// there's no per-test comparison to show) — staff-only once it reaches the web tier, since
/// it can reveal a hidden test's expected output.</summary>
public sealed record GradeResult(
    string Kind, int SubmissionId,
    string Verdict, double Score, int RuntimeMs, int MemoryKb, string CompilerOutput,
    string? FailedTest = null);

/// <summary>Progress ping while grading a <see cref="GradeJob"/>: test <see cref="Current"/>
/// (1-based) of <see cref="Total"/> is about to run.</summary>
public sealed record GradeProgress(string Kind, int SubmissionId, int Current, int Total);

/// <summary>
/// An ad-hoc compile+run. <see cref="CorrelationId"/> ties the result (delivered via
/// <see cref="IJudgeJobSource.ReportRunResultAsync"/>) back to the awaiting producer.
/// </summary>
public sealed record RunJob(
    string Language, string Code, string Stdin,
    int TimeLimitMs, int MemoryLimitKb, string CorrelationId,
    string? InputFileName = null) : JudgeJob;

/// <summary>Producer side — used by the web controllers.</summary>
public interface IJudgeQueue
{
    /// <summary>Fire-and-forget: a submission is graded and the verdict persisted by the web tier.</summary>
    ValueTask EnqueueGradeAsync(GradeJob job, CancellationToken ct = default);

    /// <summary>Request/response: enqueue an ad-hoc run and await its result.</summary>
    Task<RunResultDto> EnqueueRunAsync(
        string language, string code, string stdin, int timeLimitMs, int memoryLimitKb,
        string? inputFileName = null, CancellationToken ct = default);
}

/// <summary>Consumer side — used by <see cref="JudgeWorker"/>.</summary>
public interface IJudgeJobSource
{
    IAsyncEnumerable<JudgeJob> ReadJobsAsync(CancellationToken ct);
    ValueTask ReportRunResultAsync(RunJob job, RunResultDto result);
    ValueTask ReportGradeResultAsync(GradeResult result);
    ValueTask ReportGradeProgressAsync(GradeProgress progress);
}

/// <summary>Grade-result side — consumed by the web tier to persist verdicts + notify.</summary>
public interface IGradeResultStream
{
    IAsyncEnumerable<GradeResult> ReadResultsAsync(CancellationToken ct);
    IAsyncEnumerable<GradeProgress> ReadProgressAsync(CancellationToken ct);
}

/// <summary>
/// Single-process queue: bounded <see cref="Channel{T}"/>s plus a registry matching a
/// <see cref="RunJob.CorrelationId"/> to its awaiting <c>TaskCompletionSource</c>.
/// </summary>
public sealed class InProcessJudgeQueue : IJudgeQueue, IJudgeJobSource, IGradeResultStream
{
    private readonly Channel<JudgeJob> _jobs;
    private readonly Channel<GradeResult> _grades =
        Channel.CreateUnbounded<GradeResult>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<GradeProgress> _progress =
        Channel.CreateUnbounded<GradeProgress>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunResultDto>> _pending = new();

    public InProcessJudgeQueue(IOptions<JudgeOptions> options)
    {
        _jobs = Channel.CreateBounded<JudgeJob>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    /// <summary>Jobs waiting to be picked up by the (single, in-process) judge worker — for
    /// the admin system-status panel. -1 if the runtime can't report a count.</summary>
    public int PendingJobCount => _jobs.Reader.CanCount ? _jobs.Reader.Count : -1;

    public ValueTask EnqueueGradeAsync(GradeJob job, CancellationToken ct = default) =>
        _jobs.Writer.WriteAsync(job, ct);

    public async Task<RunResultDto> EnqueueRunAsync(
        string language, string code, string stdin, int timeLimitMs, int memoryLimitKb,
        string? inputFileName = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<RunResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await _jobs.Writer.WriteAsync(new RunJob(language, code, stdin, timeLimitMs, memoryLimitKb, id, inputFileName), ct);
            return await tcs.Task.WaitAsync(ct);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    public IAsyncEnumerable<JudgeJob> ReadJobsAsync(CancellationToken ct) => _jobs.Reader.ReadAllAsync(ct);

    public ValueTask ReportRunResultAsync(RunJob job, RunResultDto result)
    {
        if (_pending.TryGetValue(job.CorrelationId, out var tcs)) tcs.TrySetResult(result);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReportGradeResultAsync(GradeResult result) => _grades.Writer.WriteAsync(result);
    public ValueTask ReportGradeProgressAsync(GradeProgress progress) => _progress.Writer.WriteAsync(progress);

    public IAsyncEnumerable<GradeResult> ReadResultsAsync(CancellationToken ct) => _grades.Reader.ReadAllAsync(ct);
    public IAsyncEnumerable<GradeProgress> ReadProgressAsync(CancellationToken ct) => _progress.Reader.ReadAllAsync(ct);
}
