using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeeCoding.Controllers;

[Authorize]
[Route("api/run")]
public class RunController(IJudgeQueue queue, RateLimiter rate, IOptions<JudgeOptions> opt,
    AppDbContext db, BoardService boards, VisibilityService vis, AdminAccess admin) : ApiControllerBase
{
    private readonly IJudgeQueue _queue = queue;
    private readonly RateLimiter _rate = rate;
    private readonly JudgeOptions _opt = opt.Value;
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;

    /// <summary>Ad-hoc compile + run with custom stdin. Not judged, not persisted.</summary>
    [HttpPost]
    [RequestSizeLimit(BodyLimits.CodeBodyBytes)]
    public async Task<ActionResult<RunResultDto>> Run(RunDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return BadRequest("Code is empty.");
        if (dto.Code.Length > 200_000) return BadRequest("Code is too large.");
        if ((dto.Stdin?.Length ?? 0) > 256_000) return BadRequest("Stdin is too large.");
        if (!_rate.TryAcquire(UserId)) return StatusCode(429, "Slow down a moment and try again.");

        // If this Run is tied to a problem, apply that problem's restrictions here too
        // (Submit already does) so students can't sidestep them via the Run box — including
        // file-input mode, so "Run" behaves the same as "Submit" for such problems.
        var (bh, bs, allowedLangs, inputFileName) = await RestrictionsForAsync(dto.ProblemId, dto.BankProblemId);
        if (SourcePolicy.Violation(dto.Code, bh, bs) is { } denied)
            return new RunResultDto(false, denied, "", "", 0, 0, false, 0, 0);
        if (!Languages.Allows(allowedLangs, dto.Language))
            return new RunResultDto(false, $"This problem only accepts {Languages.Label(allowedLangs)}.", "", "", 0, 0, false, 0, 0);

        // Playground / Live code have no problem to dictate file input, so let the caller ask
        // for it; a problem's own setting always wins.
        if (dto.ProblemId is null && dto.BankProblemId is null)
            inputFileName = InputFilePolicy.Normalize(dto.InputFileName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var r = await _queue.EnqueueRunAsync(
                dto.Language, dto.Code, dto.Stdin ?? "", _opt.RunTimeLimitMs, _opt.RunMemoryLimitKb,
                inputFileName, timeout.Token);
            return r with { Stdout = CutOutput(r.Stdout), Stderr = CutOutput(r.Stderr) };
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, "The judge is busy. Try again shortly.");
        }
    }

    // The judge keeps up to 4 MB of output, and a loop that prints forever fills all of it. A Run box has room for a screenful, so the browser
    // gets the head only (judging is unaffected; this is just the Run reply).
    private const int MaxRunChars = 100_000;
    internal static string CutOutput(string s) => s.Length <= MaxRunChars ? s : s[..MaxRunChars] + $"\n…[output cut after {MaxRunChars:N0} characters]";

    private async Task<(string? Headers, string? Symbols, string? AllowedLanguages, string? InputFileName)> RestrictionsForAsync(int? problemId, int? bankProblemId)
    {
        if (bankProblemId is int bid)
        {
            var b = await _db.BankProblems.Where(b => b.Id == bid && (b.IsPublic || b.OwnerId == UserId))
                .Select(b => new { b.BannedHeaders, b.BannedSymbols, b.AllowedLanguages, b.InputFileName }).FirstOrDefaultAsync();
            return b is null ? (null, null, null, null) : (b.BannedHeaders, b.BannedSymbols, b.AllowedLanguages, b.InputFileName);
        }
        if (problemId is int pid)
        {
            var p = await _db.Problems.Include(p => p.Group).FirstOrDefaultAsync(p => p.Id == pid);
            if (p is null) return (null, null, null, null);
            var m = await _boards.GetMembershipAsync(p.BoardId, UserId);
            if (m is null) return (null, null, null, null);
            // Ids are sequential: a hidden or not-yet-open problem must not be probed through the rules it enforces (same check as Submit).
            if (!vis.IsStaff(m.Role) && !IsAdminUser(admin) && !GroupAccess.StudentVisible(p, DateTime.UtcNow)) return (null, null, null, null);
            return (p.BannedHeaders, p.BannedSymbols, p.AllowedLanguages, p.InputFileName);
        }
        return (null, null, null, null);
    }
}
