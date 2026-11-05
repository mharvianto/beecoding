using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
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
public partial class ImagesController(AppDbContext db, PlatformRuntimeConfig cfg) : ApiControllerBase
{
    private const long HardUploadBytes = 3 * 1024 * 1024;   // backstop for the request itself; the real limit is the admin's ImageMaxKb (at most 3000)
    private const int MaxWidth = 1200;
    // A tiny file can describe an enormous picture (a 240 KB PNG of 16000 x 16000 pixels is ~1 GB decoded). 24 M pixels is ~96 MB decoded:
    // room for a 12-24 MP photo (which the size limit would reject anyway), nowhere near enough to hurt the server.
    private const long MaxPixels = 24_000_000;
    private readonly AppDbContext _db = db;
    private readonly PlatformRuntimeConfig _cfg = cfg;

    [GeneratedRegex(@"/api/images/([A-Za-z0-9_-]{22})")]
    private static partial Regex RefPattern();

    /// <summary>The image ids a statement refers to.</summary>
    public static IEnumerable<string> RefsIn(string? markdown) =>
        RefPattern().Matches(markdown ?? "").Select(m => m.Groups[1].Value).Distinct();

    [HttpPost]
    [RequestSizeLimit(HardUploadBytes + 64 * 1024)]
    public async Task<ActionResult<object>> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0) return BadRequest("Choose an image file.");
        var limit = _cfg.ImageMaxKb * 1024L;
        if (file.Length > limit)   // the upload itself is checked first; only an accepted file is compressed
            return BadRequest($"The image is {(file.Length + 1023) / 1024} KB, over the {_cfg.ImageMaxKb} KB limit. Choose a smaller file.");
        // Unused pictures are swept by StatementImageJanitor after Images:OrphanRetentionDays, so this cap only bites on pictures in use.
        var perUser = _cfg.ImageMaxPerUser;
        if (await _db.StatementImages.CountAsync(x => x.OwnerId == UserId) >= perUser)
            return Conflict(perUser == 0 ? "Picture uploads are turned off." : $"You have reached the limit of {perUser} images.");

        // Read the size from the header first: decoding comes only after we know how much memory it will take.
        using var codec = SKCodec.Create(file.OpenReadStream());
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return BadRequest("That file is not a PNG, JPEG, GIF or WebP image.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            return BadRequest($"The picture is {codec.Info.Width} x {codec.Info.Height} pixels, too large. Shrink it to under {MaxPixels / 1_000_000} megapixels first.");
        using var bmp = SKBitmap.Decode(codec);
        if (bmp is null || bmp.Width == 0 || bmp.Height == 0) return BadRequest("That file is not a PNG, JPEG, GIF or WebP image.");

        var stored = Shrink(bmp, file.Length);
        if (stored.Length > limit) stored = Compress(bmp, limit) ?? stored;   // rare: only when the re-encode came out bigger than the limit
        if (stored.Length > limit) return BadRequest("This image cannot be compressed enough. Use a smaller or simpler picture.");

        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _db.StatementImages.Add(new StatementImage { Id = id, OwnerId = UserId, Data = stored });
        await _db.SaveChangesAsync();
        return new { id, url = $"/api/images/{id}", kb = (stored.Length + 1023) / 1024 };
    }

    /// <summary>The normal compression of an accepted upload: at most <see cref="MaxWidth"/> px wide and re-encoded. Lossless PNG
    /// keeps diagrams crisp; only when that came out bigger than the file the author sent (a photo) is JPEG used instead, if smaller.</summary>
    private static byte[] Shrink(SKBitmap bmp, long uploadedBytes)
    {
        using var resized = bmp.Width <= MaxWidth ? null
            : bmp.Resize(new SKImageInfo(MaxWidth, Math.Max(1, bmp.Height * MaxWidth / bmp.Width)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var src = resized ?? bmp;
        using var img = SKImage.FromBitmap(src);
        var best = img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        if (best.Length <= uploadedBytes) return best;

        using var flat = new SKBitmap(src.Width, src.Height);   // JPEG has no transparency: flatten on white
        using (var canvas = new SKCanvas(flat)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(src, 0, 0); }
        using var flatImg = SKImage.FromBitmap(flat);
        var jpg = flatImg.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
        return jpg.Length < best.Length ? jpg : best;
    }

    /// <summary>Last resort when <see cref="Shrink"/> is still over the limit. Lossless PNG first (diagrams stay crisp); if that is too big, JPEG at falling quality (flattened on white,
    /// since JPEG has no transparency); if still too big, shrink by a quarter and try again.</summary>
    private static byte[]? Compress(SKBitmap bmp, long maxBytes)
    {
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        int w = Math.Min(bmp.Width, MaxWidth);
        for (int round = 0; round < 6 && w >= 100; round++, w = w * 3 / 4)
        {
            using var resized = w == bmp.Width ? null : bmp.Resize(new SKImageInfo(w, Math.Max(1, (int)((long)bmp.Height * w / bmp.Width))), sampling);
            var src = resized ?? bmp;
            using (var img = SKImage.FromBitmap(src))
            using (var png = img.Encode(SKEncodedImageFormat.Png, 100))
                if (png.Size <= maxBytes) return png.ToArray();

            using var flat = new SKBitmap(src.Width, src.Height);
            using (var canvas = new SKCanvas(flat)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(src, 0, 0); }
            using var flatImg = SKImage.FromBitmap(flat);
            foreach (var quality in new[] { 85, 65 })
                using (var jpg = flatImg.Encode(SKEncodedImageFormat.Jpeg, quality))
                    if (jpg.Size <= maxBytes) return jpg.ToArray();
        }
        return null;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var data = await _db.StatementImages.Where(x => x.Id == id).Select(x => x.Data).FirstOrDefaultAsync();
        if (data is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";   // ids are never reused, so a cached copy is never stale
        return File(data, data[0] == 0xFF ? "image/jpeg" : "image/png");   // stored as PNG, or JPEG when that was the only way to fit the size limit
    }
}
