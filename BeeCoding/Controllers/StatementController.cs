using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[ApiController]
[Authorize]
public class StatementController(AppDbContext db, BoardService boards, StatementImageService images, VisibilityService vis, AuditLog audit,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache, IConfiguration cfg) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly StatementImageService _images = images;
    private readonly VisibilityService _vis = vis;

    /// <summary>
    /// A browser that says it is automated (<c>X-Client-Flags</c>, from lib/automation.js: navigator.webdriver, headless Chrome) is refused the
    /// protected statement when the viewer is a student, and an audit entry says who tried (once per person and problem an hour). The header is
    /// the client's own word, so this stops casual scripts only — never a person driving a normal browser, or a model reading screenshots.
    /// Off with <c>Security:BlockAutomatedBrowsers=false</c>.
    /// </summary>
    private async Task<ActionResult?> RefuseAutomationAsync(string what, int id, bool isStudent)
    {
        var flags = Request.Headers["X-Client-Flags"].ToString();
        if (!isStudent || flags.Length == 0 || !cfg.GetValue("Security:BlockAutomatedBrowsers", true)) return null;
        var seen = $"automation:{UserId}:{what}{id}";
        if (!cache.TryGetValue(seen, out _))
        {
            cache.Set(seen, true, TimeSpan.FromHours(1));
            await audit.RecordAsync(UserId, ActorEmail, "automation-blocked", "Statement", id, $"{what} · {(flags.Length > 60 ? flags[..60] : flags)}");
        }
        return StatusCode(403, "Automated browsers cannot open protected problems. Use a regular browser window.");
    }

    /// <summary>
    /// Board problem statement + samples rendered to a PNG (no text reaches the client),
    /// returned AES-GCM encrypted with a per-viewer identity watermark baked in.
    /// </summary>
    [HttpGet("api/problems/{problemId:int}/statement")]
    public async Task<ActionResult<object>> BoardProblem(int problemId, [FromQuery] string? theme)
    {
        var problem = await _db.Problems.Include(p => p.TestCases).Include(p => p.Group)
            .FirstOrDefaultAsync(p => p.Id == problemId);
        if (problem is null) return NotFound();
        var me = await _boards.GetMembershipAsync(problem.BoardId, UserId);
        if (me is null) return Forbid();
        // Same rule as the problem itself (ProblemsController.Get): a hidden problem, or one in a hidden / not-yet-open group, is
        // not there for students — ids are sequential, so without this the statement and its samples could be read ahead of time.
        if (!_vis.IsStaff(me.Role) && !GroupAccess.StudentVisible(problem, DateTime.UtcNow)) return NotFound();
        if (await RefuseAutomationAsync("board", problemId, !_vis.IsStaff(me.Role)) is { } refused) return refused;

        var samples = problem.TestCases.Where(t => t.IsSample)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => (t.Stdin, t.ExpectedStdout)).ToList();

        return await EncryptedImageAsync($"b{problemId}", problem.StatementMarkdown, samples, theme);
    }

    /// <summary>Practice (public bank) problem statement — always protected.</summary>
    [HttpGet("api/practice/{id:int}/statement")]
    public async Task<ActionResult<object>> PracticeProblem(int id, [FromQuery] string? theme)
    {
        var problem = await _db.BankProblems.Include(p => p.TestCases)
            .FirstOrDefaultAsync(p => p.Id == id && (p.IsPublic || p.OwnerId == UserId));
        if (problem is null) return NotFound();
        if (await RefuseAutomationAsync("practice", id, CurrentRole == "Student") is { } refused) return refused;

        var samples = problem.TestCases.Where(t => t.IsSample)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => (t.Stdin, t.ExpectedStdout)).ToList();

        return await EncryptedImageAsync($"k{id}", problem.StatementMarkdown, samples, theme);
    }

    private async Task<object> EncryptedImageAsync(string idKey, string markdown,
        List<(string Stdin, string ExpectedStdout)> samples, string? theme)
    {
        var ids = ImagesController.RefsIn(markdown).ToList();
        var pictures = ids.Count == 0 ? new Dictionary<string, byte[]>()
            : await _db.StatementImages.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Data);
        bool dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? UserId.ToString();
        var watermark = $"{email} · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            markdown + "\0" + string.Join("", samples.Select(s => s.Stdin + "\0" + s.ExpectedStdout)))))[..16];
        var cacheKey = $"{idKey}:{fingerprint}:{(dark ? "d" : "l")}";

        var png = _images.RenderPng(cacheKey, markdown, samples, watermark, dark, pictures);

        // AES-256-GCM; fresh key + nonce per request, delivered in the same authenticated response.
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[png.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(key, 16))
            gcm.Encrypt(nonce, png, cipher, tag);

        var payload = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, payload, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, payload, cipher.Length, tag.Length);

        Response.Headers.CacheControl = "no-store, private";
        return new
        {
            alg = "AES-GCM",
            key = Convert.ToBase64String(key),
            iv = Convert.ToBase64String(nonce),
            data = Convert.ToBase64String(payload),
        };
    }
}
