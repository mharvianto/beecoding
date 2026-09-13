using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BeeCoding.Controllers;

public record AiHintDto(
    int? ProblemId, int? BankProblemId,
    string? Language, string? Code,
    string? Stdin, string? Verdict, string? CompilerOutput, string? Stderr,
    string? Question, string? Lang,     // Lang: "id" | "en" (reply language)
    string? BoardSlug = null,           // live-coding session (no problem) on this board
    string? TeacherCode = null,         // the teacher's current live buffer, for context / "explain this"
    bool Explain = false);              // student asked to explain the teacher's code

public record AiGenerateDto(string? Idea, string? Level, string? Language, int? Count, string? Lang);

[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController(AppDbContext db, BoardService boards, AiTutorService ai,
    AiUsageService usage, AiHintProgressService prog, OrgResolver orgs, IJudgeQueue queue, IOptions<AiOptions> opt,
    IAiJobStore jobs, IServiceScopeFactory scopes, ILogger<AiController> log) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly AiTutorService _ai = ai;
    private readonly AiUsageService _usage = usage;
    private readonly AiHintProgressService _prog = prog;
    private readonly OrgResolver _orgs = orgs;
    private readonly IJudgeQueue _queue = queue;
    private readonly AiOptions _opt = opt.Value;
    private readonly IAiJobStore _jobs = jobs;
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<AiController> _log = log;

    private static readonly ConcurrentDictionary<int, long> _last = new();

    [HttpGet("enabled")]
    public IActionResult Enabled() =>
        Ok(new { enabled = _ai.Available, defaultLang = _ai.DefaultReplyLanguage });

    [HttpGet("usage")]
    public async Task<IActionResult> Usage()
    {
        var orgId = await _orgs.ForUserAsync(UserId);
        return Ok(await _usage.SummaryAsync(UserId, CurrentRole, orgId));
    }

    /// <summary>
    /// Teacher-only. Ask the AI to draft a full bank problem from an idea, then compile and
    /// run its reference solution against the AI's inputs so the stored expected outputs are
    /// the program's ACTUAL output (not the model's claim). Saved to the caller's bank as
    /// private, returned for review/editing.
    /// </summary>
    [HttpPost("generate-problem")]
    public async Task<IActionResult> GenerateProblem(AiGenerateDto dto)
    {
        var orgId = await _orgs.ForUserAsync(UserId);
        _ai.UseOrganization(orgId);
        if (!_ai.Available) return NotFound();
        if (CurrentRole != "Teacher") return StatusCode(StatusCodes.Status403Forbidden, "Teachers only.");
        if (string.IsNullOrWhiteSpace(dto.Idea)) return BadRequest("Describe the idea or topic.");
        if (RateLimited()) return StatusCode(StatusCodes.Status429TooManyRequests, "Give the AI a few seconds.");
        var (allowed, reason) = await _usage.CheckGateAsync(UserId, CurrentRole, orgId);
        if (!allowed) return StatusCode(StatusCodes.Status429TooManyRequests, reason);
        if (await _jobs.RunningForAsync(UserId) >= 2)
            return StatusCode(StatusCodes.Status429TooManyRequests, "You already have generations running — wait for those to finish.");

        var wantLang = dto.Language is "c" ? "c" : "cpp";
        var level = dto.Level is "Easy" or "Medium" or "Hard" ? dto.Level! : "Medium";
        var count = dto.Count is >= 3 and <= 15 ? dto.Count!.Value : 10;
        var lang = string.IsNullOrWhiteSpace(dto.Lang) ? _ai.DefaultReplyLanguage : dto.Lang!;

        // The AI call + compiling & running the reference against N tests takes minutes, far
        // longer than a request should hang. Run it detached; the client polls the job id.
        var job = await _jobs.CreateAsync(UserId);
        _ = Task.Run(() => RunGenerateAsync(job.Id, UserId, dto.Idea!, level, wantLang, count, lang, orgId));
        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("generate-problem/{id:guid}")]
    public async Task<IActionResult> GenerateProblemStatus(Guid id)
    {
        var job = await _jobs.GetAsync(id.ToString("N"));
        if (job is null || job.UserId != UserId) return NotFound();
        return job.Status switch
        {
            "done" => Ok(new { status = "done", problem = job.Result }),
            "error" => Ok(new { status = "error", message = job.Message, compilerOutput = job.CompilerOutput, stderr = job.Stderr }),
            _ => Ok(new { status = "running" }),
        };
    }

    private async Task RunGenerateAsync(string jobId, int userId, string idea, string level, string wantLang, int count, string lang, int? organizationId)
    {
        Task Fail(string msg, string? compilerOutput = null, string? stderr = null) =>
            _jobs.FailAsync(jobId, msg, compilerOutput, stderr);

        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var ai = sp.GetRequiredService<AiTutorService>();
            ai.UseOrganization(organizationId);   // a fresh scope's own AiTutorService instance
            var usage = sp.GetRequiredService<AiUsageService>();
            var queue = sp.GetRequiredService<IJudgeQueue>();
            var ct = CancellationToken.None;   // detached from the original request

            AiTutorService.AiGenResult gen;
            try { gen = await ai.GenerateProblemAsync(idea, level, wantLang, count, lang, ct); }
            catch (AiUnavailableException ex) { await Fail(ex.Message); return; }
            await usage.RecordAsync(userId, gen.PromptTokens, gen.CompletionTokens, ct);

            var gp = gen.Problem;
            var refLang = gp.Language is "c" ? "c" : "cpp";
            var built = new List<(string Stdin, string Expected, bool IsSample)>();

            int i = 0;
            var seenStdin = new HashSet<string>();
            foreach (var t in gp.Tests.Take(15))
            {
                var stdin = t.Stdin ?? "";
                // teaching data, not a stress test — drop oversized inputs and exact duplicates
                if (stdin.Length > 16_000) continue;
                if (!seenStdin.Add(stdin.Replace("\r\n", "\n").Trim())) continue;
                RunResultDto res;
                try { res = await queue.EnqueueRunAsync(refLang, gp.ReferenceSolution, stdin, gp.TimeLimitMs, gp.MemoryLimitKb, ct: ct); }
                catch { await Fail("Timed out validating the generated problem."); return; }

                if (!res.CompileOk)
                { await Fail("The AI's reference solution didn't compile — try again or rephrase the idea.", compilerOutput: res.CompilerOutput); return; }
                if (res.TimedOut || res.Signal != 0 || res.ExitCode != 0)
                { await Fail($"The AI's reference solution failed on test #{i + 1} (signal {res.Signal}, exit {res.ExitCode}) — try again or rephrase.", stderr: res.Stderr); return; }

                built.Add((stdin, res.Stdout ?? "", t.IsSample));
                i++;
            }
            if (built.Count < 2) { await Fail("The AI didn't produce enough usable tests — try again."); return; }

            if (!built.Any(x => x.IsSample)) built[0] = (built[0].Stdin, built[0].Expected, true);
            if (built.All(x => x.IsSample)) built[^1] = (built[^1].Stdin, built[^1].Expected, false);

            var problem = new BankProblem
            {
                OwnerId = userId,
                Title = gp.Title.Trim(),
                StatementMarkdown = gp.StatementMarkdown ?? "",
                AllowedLanguages = "",          // AI problems are I/O-based — any language is fine
                GeneratedByAi = true,
                PendingReview = true,           // held back from other teachers until an admin approves
                Level = Mapping.ParseLevel(gp.Level),
                Tags = Mapping.NormalizeTags(gp.Tags),
                TimeLimitMs = gp.TimeLimitMs,
                MemoryLimitKb = gp.MemoryLimitKb,
                IsPublic = false,               // intent is still "share with other teachers" — see admin AI review queue
            };
            int pos = 0;
            foreach (var (stdin, expected, isSample) in built)
                problem.TestCases.Add(new BankTestCase
                {
                    Stdin = stdin,
                    ExpectedStdout = expected,
                    IsSample = isSample,
                    Points = isSample ? 0 : 1,
                    Position = pos++,
                });

            db.BankProblems.Add(problem);
            await db.SaveChangesAsync(ct);
            await db.Entry(problem).Reference(x => x.Owner).LoadAsync(ct);

            await _jobs.CompleteAsync(jobId, Mapping.ToDto(problem, userId));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "generate-problem job {Job} failed", jobId);
            await Fail("Generation failed unexpectedly — try again.");
        }
    }

    /// <summary>
    /// Teacher-only, own bank problem. Keep the statement + samples; replace the HIDDEN tests
    /// with a fresh, diverse set. The AI writes a new reference solution which is checked
    /// against the existing samples before its output is trusted for the new inputs.
    /// </summary>
    [HttpPost("regenerate-tests/{bankProblemId:int}")]
    public async Task<IActionResult> RegenerateTests(int bankProblemId, [FromQuery] int? count)
    {
        var orgId = await _orgs.ForUserAsync(UserId);
        _ai.UseOrganization(orgId);
        if (!_ai.Available) return NotFound();
        if (CurrentRole != "Teacher") return StatusCode(StatusCodes.Status403Forbidden, "Teachers only.");
        if (RateLimited()) return StatusCode(StatusCodes.Status429TooManyRequests, "Give the AI a few seconds.");
        var (allowed, reason) = await _usage.CheckGateAsync(UserId, CurrentRole, orgId);
        if (!allowed) return StatusCode(StatusCodes.Status429TooManyRequests, reason);
        if (await _jobs.RunningForAsync(UserId) >= 2)
            return StatusCode(StatusCodes.Status429TooManyRequests, "You already have generations running — wait for those to finish.");

        var owns = await _db.BankProblems.AnyAsync(b => b.Id == bankProblemId && b.OwnerId == UserId);
        if (!owns) return NotFound();

        var job = await _jobs.CreateAsync(UserId);
        var n = count is >= 3 and <= 15 ? count.Value : 0;
        _ = Task.Run(() => RunRegenerateAsync(job.Id, UserId, bankProblemId, n, orgId));
        return Accepted(new { jobId = job.Id });
    }

    private async Task RunRegenerateAsync(string jobId, int userId, int bankProblemId, int count, int? organizationId)
    {
        Task Fail(string msg, string? compilerOutput = null, string? stderr = null) =>
            _jobs.FailAsync(jobId, msg, compilerOutput, stderr);

        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var ai = sp.GetRequiredService<AiTutorService>();
            ai.UseOrganization(organizationId);   // a fresh scope's own AiTutorService instance
            var usage = sp.GetRequiredService<AiUsageService>();
            var queue = sp.GetRequiredService<IJudgeQueue>();
            var ct = CancellationToken.None;

            var problem = await db.BankProblems.Include(b => b.TestCases).Include(b => b.Owner)
                .FirstOrDefaultAsync(b => b.Id == bankProblemId && b.OwnerId == userId, ct);
            if (problem is null) { await Fail("Problem not found."); return; }

            var refLang = Languages.Default(problem.AllowedLanguages);
            var samples = problem.TestCases.Where(t => t.IsSample)
                .OrderBy(t => t.Position).ThenBy(t => t.Id)
                .Select(t => (t.Stdin, t.ExpectedStdout)).ToList();
            var hiddenCount = problem.TestCases.Count(t => !t.IsSample);
            var want = count > 0 ? count : Math.Clamp(hiddenCount, 3, 15);

            AiTutorService.AiRegenResult gen;
            try { gen = await ai.RegenerateTestsAsync(problem.StatementMarkdown, refLang, samples, want, ct); }
            catch (AiUnavailableException ex) { await Fail(ex.Message); return; }
            await usage.RecordAsync(userId, gen.PromptTokens, gen.CompletionTokens, ct);

            // 1) the new reference must reproduce every existing sample
            foreach (var (sIn, sExp) in samples)
            {
                RunResultDto r;
                try { r = await queue.EnqueueRunAsync(refLang, gen.ReferenceSolution, sIn, problem.TimeLimitMs, problem.MemoryLimitKb, ct: ct); }
                catch { await Fail("Timed out validating the new tests."); return; }
                if (!r.CompileOk) { await Fail("The AI's reference solution didn't compile — try again.", compilerOutput: r.CompilerOutput); return; }
                if (r.TimedOut || r.Signal != 0 || r.ExitCode != 0)
                { await Fail("The AI's reference solution crashed on a sample — try again.", stderr: r.Stderr); return; }
                if (!VerdictEvaluator.OutputMatches(r.Stdout ?? "", sExp))
                { await Fail("The AI's reference disagrees with your sample tests — the statement may be ambiguous. Tighten it and retry."); return; }
            }

            // 2) run the new inputs through the (now trusted) reference for expected output
            var seen = samples.Select(s => s.Stdin.Replace("\r\n", "\n").Trim()).ToHashSet();
            var fresh = new List<(string Stdin, string Expected)>();
            foreach (var inp in gen.Inputs.Take(15))
            {
                if (inp.Length > 16_000) continue;
                if (!seen.Add(inp.Replace("\r\n", "\n").Trim())) continue;
                RunResultDto r;
                try { r = await queue.EnqueueRunAsync(refLang, gen.ReferenceSolution, inp, problem.TimeLimitMs, problem.MemoryLimitKb, ct: ct); }
                catch { await Fail("Timed out validating the new tests."); return; }
                if (!r.CompileOk) { await Fail("The AI's reference solution didn't compile — try again.", compilerOutput: r.CompilerOutput); return; }
                if (r.TimedOut || r.Signal != 0 || r.ExitCode != 0) continue;   // skip an input the reference can't handle
                fresh.Add((inp, r.Stdout ?? ""));
            }
            if (fresh.Count < 2) { await Fail("The AI didn't produce enough usable new tests — try again."); return; }

            // replace only the hidden tests; keep samples untouched
            db.BankTestCases.RemoveRange(problem.TestCases.Where(t => !t.IsSample));
            int pos = problem.TestCases.Where(t => t.IsSample).Select(t => t.Position).DefaultIfEmpty(-1).Max() + 1;
            foreach (var (stdin, expected) in fresh)
                problem.TestCases.Add(new BankTestCase
                {
                    Stdin = stdin,
                    ExpectedStdout = expected,
                    IsSample = false,
                    Points = 1,
                    Position = pos++,
                });
            problem.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await _jobs.CompleteAsync(jobId, Mapping.ToDto(problem, userId));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "regenerate-tests job {Job} failed", jobId);
            await Fail("Regenerating tests failed unexpectedly — try again.");
        }
    }

    /// <summary>Forget the progressive-hint level for one problem — the next hint starts gentle again.</summary>
    [HttpPost("hint-progress/reset")]
    public async Task<IActionResult> ResetHintProgress(AiHintDto dto)
    {
        var key = ProblemKey(dto);
        if (key is null) return BadRequest("problemId or bankProblemId is required.");
        await _prog.ResetAsync(UserId, key, HttpContext.RequestAborted);
        return NoContent();
    }

    private static string? ProblemKey(AiHintDto dto) =>
        dto.BankProblemId is int b ? $"bank:{b}"
        : dto.ProblemId is int p ? $"board:{p}"
        : !string.IsNullOrWhiteSpace(dto.BoardSlug) ? $"live:{dto.BoardSlug}"
        : null;

    [HttpPost("hint")]
    public async Task<IActionResult> Hint(AiHintDto dto)
    {
        var orgId = await _orgs.ForHintAsync(dto.ProblemId, dto.BoardSlug, UserId);
        _ai.UseOrganization(orgId);
        if (!_ai.Available) return NotFound();
        if (RateLimited()) return StatusCode(StatusCodes.Status429TooManyRequests, "Give the AI tutor a few seconds between questions.");
        var (allowed, reason) = await _usage.CheckGateAsync(UserId, CurrentRole, orgId);
        if (!allowed) return StatusCode(StatusCodes.Status429TooManyRequests, reason);

        var (ctx, err) = await BuildContextAsync(dto);
        if (err is not null) return err;

        var level = await _prog.BumpAsync(UserId, ProblemKey(dto)!, AiHintProgressService.HashCode(dto.Code), HttpContext.RequestAborted);
        try
        {
            var r = await _ai.HintAsync(ctx!, dto.Lang, level, HttpContext.RequestAborted);
            await _usage.RecordAsync(UserId, r.PromptTokens, r.CompletionTokens);
            return Ok(new { reply = r.Text, level });
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpPost("hint/stream")]
    public async Task Stream(AiHintDto dto)
    {
        var orgId = await _orgs.ForHintAsync(dto.ProblemId, dto.BoardSlug, UserId);
        _ai.UseOrganization(orgId);
        if (!_ai.Available) { Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (RateLimited()) { Response.StatusCode = StatusCodes.Status429TooManyRequests; return; }
        var (allowed, reason) = await _usage.CheckGateAsync(UserId, CurrentRole, orgId);
        if (!allowed) { Response.StatusCode = StatusCodes.Status429TooManyRequests; await Response.WriteAsync(reason ?? ""); return; }

        var (ctx, err) = await BuildContextAsync(dto);
        if (err is not null)
        {
            Response.StatusCode = err is ObjectResult o ? o.StatusCode ?? 400 : 400;
            return;
        }

        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";   // don't let nginx buffer the stream
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var ct = HttpContext.RequestAborted;
        var level = await _prog.BumpAsync(UserId, ProblemKey(dto)!, AiHintProgressService.HashCode(dto.Code), ct);
        await WriteEventAsync(new { level }, ct);

        try
        {
            await foreach (var chunk in _ai.StreamAsync(ctx!, dto.Lang, level, ct))
            {
                if (chunk.StartsWith(AiTutorService.UsageSentinel, StringComparison.Ordinal))
                {
                    var parts = chunk[AiTutorService.UsageSentinel.Length..].Split(' ');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var pt) && int.TryParse(parts[1], out var ctk))
                        await _usage.RecordAsync(UserId, pt, ctk, CancellationToken.None);
                    continue;
                }
                bool isFinal = chunk.StartsWith(AiTutorService.FinalSentinel, StringComparison.Ordinal);
                string? final = isFinal ? chunk[AiTutorService.FinalSentinel.Length..] : null;
                string? delta = isFinal ? null : chunk;
                await WriteEventAsync(new { final, delta }, ct);
            }
            await WriteEventAsync(new { done = true }, ct);
        }
        catch (AiUnavailableException ex)
        {
            await WriteEventAsync(new { error = ex.Message }, ct);
        }
        catch (OperationCanceledException) { /* client left */ }
    }

    private async Task WriteEventAsync(object payload, CancellationToken ct)
    {
        var line = "data: " + JsonSerializer.Serialize(payload) + "\n\n";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(line), ct);
        await Response.Body.FlushAsync(ct);
    }

    private bool RateLimited()
    {
        var now = DateTime.UtcNow.Ticks;
        var min = TimeSpan.FromSeconds(Math.Max(1, _opt.RateLimitSeconds)).Ticks;
        var prev = _last.GetOrAdd(UserId, 0);
        if (now - prev < min) return true;
        _last[UserId] = now;
        return false;
    }

    private async Task<(AiHintContext? ctx, ObjectResult? err)> BuildContextAsync(AiHintDto dto)
    {
        // In a live session the student may ask about the teacher's code before writing anything.
        bool haveSomething = !string.IsNullOrWhiteSpace(dto.Code)
            || (!string.IsNullOrWhiteSpace(dto.BoardSlug) && !string.IsNullOrWhiteSpace(dto.TeacherCode));
        if (!haveSomething)
            return (null, new ObjectResult("There's no code to look at yet.") { StatusCode = 400 });
        if (dto.Code is { Length: > 200_000 })
            return (null, new ObjectResult("Code is too large.") { StatusCode = 400 });

        string statement, language;
        List<(string Stdin, string Expected)> samples;

        if (dto.BankProblemId is int bid)
        {
            var b = await _db.BankProblems.Include(x => x.TestCases)
                .FirstOrDefaultAsync(x => x.Id == bid && x.IsPublic);
            if (b is null) return (null, new ObjectResult("Problem not found.") { StatusCode = 404 });
            statement = b.StatementMarkdown; language = Languages.Default(b.AllowedLanguages);
            samples = b.TestCases.Where(t => t.IsSample).OrderBy(t => t.Position).ThenBy(t => t.Id)
                .Select(t => (t.Stdin, t.ExpectedStdout)).ToList();
        }
        else if (dto.ProblemId is int pid)
        {
            var p = await _db.Problems.Include(x => x.TestCases).FirstOrDefaultAsync(x => x.Id == pid);
            if (p is null) return (null, new ObjectResult("Problem not found.") { StatusCode = 404 });
            if (await _boards.GetMembershipAsync(p.BoardId, UserId) is null)
                return (null, new ObjectResult("Not a member of this board.") { StatusCode = 403 });
            statement = p.StatementMarkdown; language = Languages.Default(p.AllowedLanguages);
            samples = p.TestCases.Where(t => t.IsSample).OrderBy(t => t.Position).ThenBy(t => t.Id)
                .Select(t => (t.Stdin, t.ExpectedStdout)).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(dto.BoardSlug))
        {
            var boardId = await _boards.ResolveBoardIdAsync(dto.BoardSlug!);
            if (boardId is null) return (null, new ObjectResult("Board not found.") { StatusCode = 404 });
            if (await _boards.GetMembershipAsync(boardId.Value, UserId) is null)
                return (null, new ObjectResult("Not a member of this board.") { StatusCode = 403 });
            if (!await _db.Boards.Where(b => b.Id == boardId).Select(b => b.LecturingMode).FirstAsync())
                return (null, new ObjectResult("No live-coding session is running.") { StatusCode = 409 });

            var teacherCode = dto.TeacherCode is { Length: > 0 } tc ? (tc.Length > 60_000 ? tc[..60_000] : tc) : null;
            var q = dto.Question;
            if (string.IsNullOrWhiteSpace(q) && dto.Explain)
                q = "Explain what the teacher's code does, step by step.";

            var live = new AiHintContext(
                "", string.IsNullOrWhiteSpace(dto.Language) ? "cpp" : dto.Language!,
                dto.Code ?? "", dto.Stdin, dto.Verdict, dto.CompilerOutput, dto.Stderr, q,
                Array.Empty<(string, string)>(), LiveMode: true, TeacherCode: teacherCode);
            return (live, null);
        }
        else
        {
            return (null, new ObjectResult("problemId, bankProblemId or boardSlug is required.") { StatusCode = 400 });
        }

        var ctx = new AiHintContext(
            statement,
            string.IsNullOrWhiteSpace(dto.Language) ? language : dto.Language!,
            dto.Code!, dto.Stdin, dto.Verdict, dto.CompilerOutput, dto.Stderr, dto.Question, samples);
        return (ctx, null);
    }
}
