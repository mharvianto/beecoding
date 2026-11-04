using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BeeCoding.Controllers;

/// <summary>
/// Pictures for problem statements. An author uploads one and pastes <c>![](/api/images/{id})</c> into the Markdown;
/// the statement page and the protected statement PNG both fetch it by that id. Anything uploaded is decoded and
/// re-encoded as a PNG (size-capped), so only a well-formed bitmap is ever stored or served.
/// </summary>
[ApiController]
[Authorize]
[Route("api/images")]
public partial class ImagesController(AppDbContext db) : ApiControllerBase
{
    private const long MaxUploadBytes = 3 * 1024 * 1024;
    private const int MaxWidth = 1200;
    private const int MaxPerUser = 100;   // shortcut: nothing deletes unused images yet, so this is also the storage ceiling per user

    private readonly AppDbContext _db = db;

    [GeneratedRegex(@"/api/images/([A-Za-z0-9_-]{22})")]
    private static partial Regex RefPattern();

    /// <summary>The image ids a statement refers to.</summary>
    public static IEnumerable<string> RefsIn(string? markdown) =>
        RefPattern().Matches(markdown ?? "").Select(m => m.Groups[1].Value).Distinct();

    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    public async Task<ActionResult<object>> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0) return BadRequest("Choose an image file.");
        if (file.Length > MaxUploadBytes) return BadRequest("The image is too large (3 MB at most).");
        if (await _db.StatementImages.CountAsync(x => x.OwnerId == UserId) >= MaxPerUser)
            return Conflict($"You have reached the limit of {MaxPerUser} images.");

        using var bmp = SKBitmap.Decode(file.OpenReadStream());
        if (bmp is null || bmp.Width == 0 || bmp.Height == 0) return BadRequest("That file is not a PNG, JPEG, GIF or WebP image.");

        SKBitmap? scaled = null;
        try
        {
            var src = bmp;
            if (bmp.Width > MaxWidth)
            {
                scaled = bmp.Resize(new SKImageInfo(MaxWidth, Math.Max(1, bmp.Height * MaxWidth / bmp.Width)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (scaled is not null) src = scaled;
            }
            using var image = SKImage.FromBitmap(src);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            _db.StatementImages.Add(new StatementImage { Id = id, OwnerId = UserId, Data = png.ToArray() });
            await _db.SaveChangesAsync();
            return new { id, url = $"/api/images/{id}" };
        }
        finally { scaled?.Dispose(); }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var data = await _db.StatementImages.Where(x => x.Id == id).Select(x => x.Data).FirstOrDefaultAsync();
        if (data is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";   // ids are never reused, so a cached copy is never stale
        return File(data, "image/png");
    }
}
