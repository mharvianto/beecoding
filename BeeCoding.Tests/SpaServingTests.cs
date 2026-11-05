using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SpaServingTests
{
    /// <summary>The app as it runs in production, serving a throw-away web root: one real asset under /assets and an index.html.</summary>
    private static (WebApplicationFactory<Program> App, string Dir) Production(TestApp app)
    {
        var dir = Path.Combine(Path.GetTempPath(), "beecoding-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        File.WriteAllText(Path.Combine(dir, "assets", "real-abc123.js"), "console.log('real');");
        File.WriteAllText(Path.Combine(dir, "index.html"), "<!doctype html><title>spa</title>");
        return (app.WithWebHostBuilder(b => b.UseEnvironment("Production").UseWebRoot(dir).UseSetting("Judge:RequireSandbox", "false")), dir);
    }

    [Fact]
    public async Task In_production_a_real_asset_is_served_and_a_missing_one_is_a_404_that_is_not_cached()
    {
        // A MapGet("/assets/{**path}") endpoint once shadowed the real files in Production only (Development was fine): the live site went blank.
        // The live site is served under /beecoding (PathBase): the bug only showed there.
        Environment.SetEnvironmentVariable("PathBase", "/beecoding");
        using var baseApp = new TestApp();
        var (prod, dir) = Production(baseApp);
        try
        {
            using var http = prod.CreateClient();
            var real = await http.GetAsync("/beecoding/assets/real-abc123.js");
            Assert.Equal(HttpStatusCode.OK, real.StatusCode);
            Assert.Contains("real", await real.Content.ReadAsStringAsync());
            Assert.Contains("immutable", real.Headers.CacheControl?.ToString() ?? "");

            var gone = await http.GetAsync("/beecoding/assets/Leaderboard-gone123.js");
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            Assert.Contains("no-store", gone.Headers.CacheControl?.ToString() ?? "");   // Cloudflare and browsers keep a plain 404 for hours

            var deep = await http.GetAsync("/beecoding/boards/some-board");
            Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
            Assert.Contains("no-cache", deep.Headers.CacheControl?.ToString() ?? "");
        }
        finally { Environment.SetEnvironmentVariable("PathBase", null); prod.Dispose(); try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    [Fact]
    public async Task A_missing_asset_is_a_404_not_the_index_page()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        // After a deploy the old build's hashed chunks are gone. index.html with a 200 would reach the browser as a "script" of type
        // text/html and the click that wanted it would silently do nothing.
        var res = await http.GetAsync("/assets/Leaderboard-gone123.js");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task The_page_served_for_a_deep_link_revalidates_so_a_deploy_is_picked_up()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var web = app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        if (web is null || !File.Exists(Path.Combine(web, "index.html"))) return;   // the SPA is not built on this machine (CI tests before publishing)

        var res = await http.GetAsync("/boards/some-board");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-cache", res.Headers.CacheControl?.ToString() ?? "");
    }
}
