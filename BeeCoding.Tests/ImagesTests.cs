using System.Net;
using System.Net.Http.Json;
using BeeCoding.Controllers;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

public class ImageRefTests
{
    [Fact] public void Statement_image_ids_are_found_once_each()
    {
        var md = "a ![x](/api/images/AAAAAAAAAAAAAAAAAAAAAA) b ![y](/api/images/BBBBBBBBBBBBBBBBBBBBBB) ![z](/api/images/AAAAAAAAAAAAAAAAAAAAAA)";
        Assert.Equal(new[] { "AAAAAAAAAAAAAAAAAAAAAA", "BBBBBBBBBBBBBBBBBBBBBB" }, ImagesController.RefsIn(md));
        Assert.Empty(ImagesController.RefsIn("![x](https://evil.example/a.png) /api/images/short"));
    }
}

[Collection("app")]
public class ImageUploadTests
{
    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(SKColors.CornflowerBlue);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static MultipartFormDataContent File(byte[] bytes, string name)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return form;
    }

    [Fact]
    public async Task Only_real_images_are_accepted_big_ones_are_shrunk_and_anonymous_users_are_refused()
    {
        using var app = new TestApp();
        var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/images", File(Png(10, 10), "a.png"))).StatusCode);

        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

        // an SVG (script inside) and plain text are not bitmaps
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsync("/api/images", File("<svg onload=alert(1)/>"u8.ToArray(), "x.svg"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsync("/api/images", File("hello"u8.ToArray(), "x.png"))).StatusCode);

        // a 2400px-wide picture is stored at 1200px
        var ok = await teacher.PostAsync("/api/images", File(Png(2400, 100), "wide.png"));
        ok.EnsureSuccessStatusCode();
        var url = (await ok.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("url").GetString()!;
        var got = await teacher.GetAsync(url);
        Assert.Equal("image/png", got.Content.Headers.ContentType?.MediaType);
        using var stored = SKBitmap.Decode(await got.Content.ReadAsByteArrayAsync());
        Assert.Equal(1200, stored.Width);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url)).StatusCode);   // statement pictures are for signed-in users only
    }
}

[Collection("app")]
public class ImageCompressionTests
{
    private static byte[] Noise(int side)
    {
        using var bmp = new SKBitmap(side, side);
        var rnd = new Random(1);
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) bmp.SetPixel(x, y, new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256)));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient c, byte[] bytes) =>
        await c.PostAsync("/api/images", new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "p.png" } });

    [Fact]
    public async Task An_upload_over_the_limit_is_refused_and_one_under_it_is_stored_smaller()
    {
        using var app = new TestApp();
        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

        var tooBig = Noise(700);   // ~2 MB, over the default 1 MB limit
        Assert.True(tooBig.Length > 1024 * 1024);
        var refused = await Upload(teacher, tooBig);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("over the 1024 KB limit", await refused.Content.ReadAsStringAsync());

        var fits = Noise(450);     // a photo-like PNG under the limit
        Assert.InRange(fits.Length, 1, 1024 * 1024);
        var ok = await Upload(teacher, fits);
        ok.EnsureSuccessStatusCode();
        var url = (await ok.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("url").GetString()!;
        var got = await teacher.GetAsync(url);
        var stored = await got.Content.ReadAsByteArrayAsync();
        Assert.True(stored.Length < fits.Length);                                  // compressed, not just copied
        Assert.Equal("image/jpeg", got.Content.Headers.ContentType?.MediaType);    // PNG would have been bigger than the upload
        Assert.NotNull(SKBitmap.Decode(stored));
    }
}

[Collection("app")]
public class ImageJanitorTests
{
    [Fact]
    public async Task A_picture_goes_a_few_days_after_it_became_unused_and_comes_back_if_a_statement_names_it_again()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeeCoding.Data.AppDbContext>();
        var owner = await db.Users.FirstAsync();
        var board = await db.Boards.FirstAsync();
        var now = DateTime.UtcNow;
        string Id(char c) => new string(c, 22);
        BeeCoding.Models.StatementImage Img(char c, double uploadedDaysAgo, double? unusedDaysAgo = null) => new()
        {
            Id = Id(c), OwnerId = owner.Id, Data = [1], CreatedAt = now.AddDays(-uploadedDaysAgo),
            UnusedSince = unusedDaysAgo is null ? null : now.AddDays(-unusedDaysAgo.Value),
        };

        db.StatementImages.AddRange(
            Img('A', 30, 5),     // unused for 5 days: deleted
            Img('B', 30),        // uploaded long ago and in use until just now: only now noticed, so it stays for the grace period
            Img('C', 30, 1),     // unused for a day: stays
            Img('D', 30, 5),     // was unused, but a statement names it again: kept, clock reset
            Img('E', 30, 5),     // same, named only by a soft-deleted board problem (restorable)
            Img('F', 0.04));     // uploaded minutes ago, never used: gets its clock started, not deleted
        db.BankProblems.Add(new BeeCoding.Models.BankProblem { OwnerId = owner.Id, Title = "b", StatementMarkdown = $"text ![x](/api/images/{Id('D')}) more" });
        db.Problems.Add(new BeeCoding.Models.Problem { BoardId = board.Id, Title = "gone", StatementMarkdown = $"![](/api/images/{Id('E')})", DeletedAt = now });
        await db.SaveChangesAsync();

        var janitor = new BeeCoding.Services.StatementImageJanitor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BeeCoding.Services.StatementImageJanitor>.Instance);
        Assert.Equal(1, await janitor.SweepAsync(CancellationToken.None));

        var left = await db.StatementImages.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.UnusedSince);
        Assert.False(left.ContainsKey(Id('A')));
        Assert.Equal(new[] { 'B', 'C', 'D', 'E', 'F' }.Select(Id).OrderBy(x => x), left.Keys.OrderBy(x => x));
        Assert.NotNull(left[Id('B')]);   // clock started by this sweep
        Assert.True(left[Id('B')] > now.AddMinutes(-1));
        Assert.NotNull(left[Id('F')]);
        Assert.Null(left[Id('D')]);      // back in use
        Assert.Null(left[Id('E')]);
        Assert.True(left[Id('C')] < now.AddHours(-23));   // untouched: still counting from a day ago
    }
}

[Collection("app")]
public class ImageUsageTests
{
    [Fact]
    public async Task Usage_is_totalled_per_uploader_heaviest_first_and_can_be_limited_to_some_pictures()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeeCoding.Data.AppDbContext>();
        var cfg = app.Services.GetRequiredService<BeeCoding.Services.PlatformRuntimeConfig>();
        var teacher = await db.Users.FirstAsync();
        var student = new BeeCoding.Models.User { Email = "u@test.dev", DisplayName = "U", Role = BeeCoding.Models.UserRole.Student, PasswordHash = "x" };
        db.Users.Add(student);
        await db.SaveChangesAsync();
        string Id(char c) => new string(c, 22);
        BeeCoding.Models.StatementImage Img(char c, int owner, int bytes, bool unused = false) =>
            new() { Id = Id(c), OwnerId = owner, Data = new byte[bytes], UnusedSince = unused ? DateTime.UtcNow : null };
        db.StatementImages.AddRange(Img('A', teacher.Id, 1000), Img('B', teacher.Id, 2000, unused: true), Img('C', student.Id, 5000));
        await db.SaveChangesAsync();

        var all = await BeeCoding.Services.ImageUsage.BuildAsync(db, db.StatementImages, cfg);
        Assert.Equal((3, 8000L, 1, 2000L, 2), (all.Images, all.Bytes, all.Unused, all.UnusedBytes, all.Users));
        Assert.Equal(new[] { "u@test.dev", "teacher@demo.test" }, all.Top.Select(u => u.Email));   // heaviest first
        var t = all.Top[1];
        Assert.Equal((2, 3000L, 1, 2000L), (t.Images, t.Bytes, t.Unused, t.UnusedBytes));
        Assert.Equal(cfg.ImageMaxPerUser, all.LimitPerUser);

        // an organization sees only its members' pictures
        var onlyStudent = await BeeCoding.Services.ImageUsage.BuildAsync(db, db.StatementImages.Where(i => i.OwnerId == student.Id), cfg);
        Assert.Equal((1, 5000L, 1), (onlyStudent.Images, onlyStudent.Bytes, onlyStudent.Users));
    }
}
