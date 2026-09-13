using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Token-authed bulk maintenance of the public problem bank — meant for scripting, not the UI.
/// Every response is <c>404</c> unless <c>Admin:Token</c> is configured AND the request carries
/// the right token (header <c>X-Admin-Token</c>, or <c>?token=</c>) — a wrong token is
/// indistinguishable from the feature being off. Brute force is throttled per client IP.
/// Problems are upserted by (owner, title) so a script can be re-run idempotently.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin")]
public class AdminController(AppDbContext db, IConfiguration cfg) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IConfiguration _cfg = cfg;

    // per-IP failed-attempt throttle: max 8 misses per 10-minute rolling window.
    private static readonly ConcurrentDictionary<string, (int Count, long WindowTicks)> _fails = new();
    private const int MaxFails = 8;
    private static readonly long WindowTicks = TimeSpan.FromMinutes(10).Ticks;

    private ActionResult? Gate()
    {
        var expected = _cfg["Admin:Token"];
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var now = DateTime.UtcNow.Ticks;

        var st = _fails.GetValueOrDefault(ip);
        if (now - st.WindowTicks > WindowTicks) st = (0, now);   // window expired -> reset
        if (st.Count >= MaxFails) return NotFound();             // locked out (still looks "off")

        if (string.IsNullOrWhiteSpace(expected)) return NotFound();   // feature off

        var got = Request.Headers["X-Admin-Token"].ToString();
        if (string.IsNullOrEmpty(got)) got = Request.Query["token"].ToString();

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(got), Encoding.UTF8.GetBytes(expected));
        if (ok)
        {
            _fails.TryRemove(ip, out _);
            return null;
        }
        _fails[ip] = (st.Count + 1, st.WindowTicks == 0 ? now : st.WindowTicks);
        return NotFound();   // never reveal "wrong token" vs "disabled"
    }

    private async Task<User?> ResolveOwnerAsync(string? email)
    {
        var q = _db.Users.Where(u => u.Role == UserRole.Teacher);
        q = string.IsNullOrWhiteSpace(email)
            ? q.OrderBy(u => u.Id)
            : q.Where(u => u.Email == email!.Trim());
        return await q.FirstOrDefaultAsync();
    }

    /// <summary>Quick auth check.</summary>
    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        if (Gate() is { } fail) return fail;
        var owner = await ResolveOwnerAsync(null);
        return Ok(new { ok = true, defaultOwner = owner?.Email, bankCount = await _db.BankProblems.CountAsync() });
    }

    /// <summary>List every bank problem (all owners), newest first.</summary>
    [HttpGet("bank-problems")]
    public async Task<ActionResult<IEnumerable<AdminBankRow>>> List()
    {
        if (Gate() is { } fail) return fail;
        var rows = await _db.BankProblems.Include(b => b.TestCases)
            .OrderByDescending(b => b.UpdatedAt).ToListAsync();
        return rows.Select(Row).ToList();
    }

    /// <summary>Create or update a batch of bank problems.</summary>
    [HttpPost("bank-problems")]
    public async Task<ActionResult<AdminIngestResultDto>> Ingest(AdminIngestDto dto)
    {
        if (Gate() is { } fail) return fail;

        var owner = await ResolveOwnerAsync(dto.OwnerEmail);
        if (owner is null)
            return BadRequest(dto.OwnerEmail is null ? "no teacher account exists" : $"no teacher with email '{dto.OwnerEmail}'");

        bool replace = dto.ReplaceExisting ?? true;
        var created = new List<AdminBankRow>();
        var updated = new List<AdminBankRow>();
        var errors = new List<string>();

        foreach (var p in dto.Problems ?? new())
        {
            var title = (p.Title ?? "").Trim();
            try
            {
                if (title.Length == 0) { errors.Add("(untitled): Title is required"); continue; }
                var tests = p.Tests ?? new();
                if (tests.Count == 0) { errors.Add($"{title}: at least one test is required"); continue; }
                if (!tests.Any(t => !(t.IsSample ?? false) && (t.Points ?? 1) > 0))
                { errors.Add($"{title}: needs at least one scoring (non-sample, points>0) test"); continue; }

                var existing = await _db.BankProblems.Include(b => b.TestCases)
                    .FirstOrDefaultAsync(b => b.OwnerId == owner.Id && b.Title == title);

                if (existing is not null && !replace)
                { errors.Add($"{title}: already exists (replaceExisting=false)"); continue; }

                var b = existing ?? new BankProblem { OwnerId = owner.Id, CreatedAt = DateTime.UtcNow };
                b.Title = title;
                b.StatementMarkdown = p.StatementMarkdown ?? "";
                b.AllowedLanguages = Languages.Normalize(p.AllowedLanguages);
                b.Level = Mapping.ParseLevel(p.Level);
                b.Tags = Mapping.NormalizeTags(p.Tags);
                b.BannedHeaders = SourcePolicy.Normalize(p.BannedHeaders);
                b.BannedSymbols = SourcePolicy.NormalizeSymbols(p.BannedSymbols);
                b.InputFileName = InputFilePolicy.Normalize(p.InputFileName);
                b.TimeLimitMs = Math.Clamp((p.TimeLimitMs ?? 0) <= 0 ? 1000 : p.TimeLimitMs!.Value, 100, 10_000);
                b.MemoryLimitKb = Math.Clamp((p.MemoryLimitKb ?? 0) <= 0 ? 32_768 : p.MemoryLimitKb!.Value, 4_096, 512_000);
                b.IsPublic = p.IsPublic ?? true;
                b.UpdatedAt = DateTime.UtcNow;

                if (existing is not null) _db.BankTestCases.RemoveRange(existing.TestCases);
                b.TestCases = tests.Select((t, i) => new BankTestCase
                {
                    Stdin = t.Stdin ?? "",
                    ExpectedStdout = t.ExpectedStdout ?? "",
                    IsSample = t.IsSample ?? false,
                    Points = Math.Max(0, t.Points ?? (t.IsSample ?? false ? 0 : 1)),
                    Position = t.Position ?? i,
                }).ToList();

                if (existing is null) _db.BankProblems.Add(b);
                await _db.SaveChangesAsync();

                (existing is null ? created : updated).Add(Row(b));
            }
            catch (Exception ex)
            {
                errors.Add($"{(title.Length == 0 ? "(untitled)" : title)}: {ex.Message}");
            }
        }

        return new AdminIngestResultDto(created, updated, errors);
    }

    /// <summary>Delete one bank problem (and its submissions cascade via EF).</summary>
    [HttpDelete("bank-problems/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (Gate() is { } fail) return fail;
        var b = await _db.BankProblems.FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();
        _db.BankProblems.Remove(b);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static AdminBankRow Row(BankProblem b) => new(
        b.Id, b.Title, b.AllowedLanguages, b.Level.ToString(), b.Tags, b.IsPublic,
        b.TestCases.Count, b.TestCases.Count(t => t.IsSample), b.UpdatedAt);
}
