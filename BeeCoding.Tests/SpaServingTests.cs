using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SpaServingTests
{
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
