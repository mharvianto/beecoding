using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[ApiController]
[Authorize]
public class StatementController(AppDbContext db, BoardService boards, StatementImageService images, VisibilityService vis) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly StatementImageService _images = images;
    private readonly VisibilityService _vis = vis;

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
