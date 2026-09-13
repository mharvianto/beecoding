using BeeCoding.Models;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Judge;

/// <summary>
/// Pure compute: pull jobs, compile + run them in a sandbox, report the result. Holds no
/// database or notification dependency, so it can run as its own low-privilege process
/// (see BeeCoding.Judge / DEPLOY.md §2.6).
/// </summary>
public sealed class JudgeWorker(
    IJudgeJobSource source, NativeCompiler compiler, NativeSandbox sandbox,
    NativeToolchain toolchain, IOptions<JudgeOptions> opt, ILogger<JudgeWorker> log) : BackgroundService
{
    private readonly IJudgeJobSource _source = source;
    private readonly NativeCompiler _compiler = compiler;
    private readonly NativeSandbox _sandbox = sandbox;
    private readonly NativeToolchain _toolchain = toolchain;
    private readonly JudgeOptions _opt = opt.Value;
    private readonly ILogger<JudgeWorker> _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _toolchain.Initialize();
        using var slots = new SemaphoreSlim(_opt.MaxConcurrent);

        await foreach (var job in _source.ReadJobsAsync(stoppingToken))
        {
            await slots.WaitAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    switch (job)
                    {
                        case RunJob r: await ProcessRunAsync(r, stoppingToken); break;
                        case GradeJob g: await ProcessGradeAsync(g, stoppingToken); break;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "judge job crashed");
                    if (job is RunJob rj)
                        await _source.ReportRunResultAsync(rj, new RunResultDto(false, "internal judge error", "", "", 0, 0, false, 0, 0));
                    else if (job is GradeJob gj)
                        await _source.ReportGradeResultAsync(new GradeResult(gj.Kind, gj.SubmissionId, nameof(Verdict.RuntimeError), 0, 0, 0, "internal judge error"));
                }
                finally { slots.Release(); }
            }, stoppingToken);
        }
    }

    private string NewWorkDir()
    {
        var dir = Path.Combine(_opt.WorkRoot, "job_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanUp(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    // ---------- ad-hoc run ----------
    private async Task ProcessRunAsync(RunJob job, CancellationToken ct)
    {
        var dir = NewWorkDir();
        try
        {
            var compile = await _compiler.CompileAsync(dir, job.Language, job.Code, ct);
            if (!compile.Ok)
            {
                await _source.ReportRunResultAsync(job, new RunResultDto(false, compile.Output, "", "", 0, 0, false, 0, 0));
                return;
            }

            var exec = await _sandbox.ExecuteAsync(dir, compile.ExePath!, job.Stdin ?? "", job.TimeLimitMs, job.MemoryLimitKb, ct, job.InputFileName);
            var verdict = VerdictEvaluator.ClassifyRun(exec, job.TimeLimitMs, job.MemoryLimitKb);

            await _source.ReportRunResultAsync(job, new RunResultDto(
                true, compile.Output, exec.Stdout, exec.Stderr, exec.WallMs, exec.PeakKb,
                verdict == Verdict.TimeLimit, exec.ExitCode, exec.Signal));
        }
        finally { CleanUp(dir); }
    }

    // ---------- submission grading ----------
    private async Task ProcessGradeAsync(GradeJob job, CancellationToken ct)
    {
        var dir = NewWorkDir();
        try
        {
            var (verdict, score, ms, kb, compilerOut) = await GradeAsync(dir, job, ct);
            await _source.ReportGradeResultAsync(new GradeResult(
                job.Kind, job.SubmissionId, verdict.ToString(), score, ms, kb, compilerOut));
        }
        finally { CleanUp(dir); }
    }

    private async Task<(Verdict Verdict, double Score, int MaxMs, int MaxKb, string CompilerOutput)> GradeAsync(
        string dir, GradeJob job, CancellationToken ct)
    {
        if (SourcePolicy.Violation(job.Code, job.BannedHeaders, job.BannedSymbols) is { } denied)
            return (Verdict.CompileError, 0, 0, 0, denied);

        var compile = await _compiler.CompileAsync(dir, job.Language, job.Code, ct);
        if (!compile.Ok)
            return (Verdict.CompileError, 0, 0, 0, compile.Output);

        int totalPoints = Math.Max(1, job.Tests.Sum(t => Math.Max(0, t.Points)));
        int passedPoints = 0, maxMs = 0, maxKb = 0;
        Verdict verdict = Verdict.Accepted;

        foreach (var t in job.Tests)
        {
            var exec = await _sandbox.ExecuteAsync(dir, compile.ExePath!, t.Stdin ?? "", job.TimeLimitMs, job.MemoryLimitKb, ct, job.InputFileName);
            maxMs = Math.Max(maxMs, exec.WallMs);
            maxKb = Math.Max(maxKb, exec.PeakKb);

            var cls = VerdictEvaluator.ClassifyRun(exec, job.TimeLimitMs, job.MemoryLimitKb);
            if (cls != Verdict.Accepted) { verdict = cls; break; }
            if (!VerdictEvaluator.OutputMatches(exec.Stdout, t.Expected)) { verdict = Verdict.WrongAnswer; break; }
            passedPoints += Math.Max(0, t.Points);
        }

        double score = verdict == Verdict.Accepted ? 1.0 : (double)passedPoints / totalPoints;
        return (verdict, score, maxMs, maxKb, "");
    }
}
