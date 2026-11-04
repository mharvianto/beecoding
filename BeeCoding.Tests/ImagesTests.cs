using System.Net;
using System.Net.Http.Json;
using BeeCoding.Controllers;
using SkiaSharp;
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
