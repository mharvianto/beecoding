using System.Net;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class CsrfTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    public CsrfTests(TestApp app) => _app = app;

    private async Task<HttpStatusCode> Post(string path, string? site)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (site is not null) req.Headers.Add("Sec-Fetch-Site", site);
        return (await _app.CreateClient().SendAsync(req)).StatusCode;
    }

    [Fact] public async Task Cross_site_post_is_refused() => Assert.Equal(HttpStatusCode.Forbidden, await Post("/api/auth/logout", "cross-site"));
    [Fact] public async Task Same_origin_post_passes() => Assert.NotEqual(HttpStatusCode.Forbidden, await Post("/api/auth/logout", "same-origin"));
    [Fact] public async Task Scripts_without_the_header_pass() => Assert.NotEqual(HttpStatusCode.Forbidden, await Post("/api/auth/logout", null));
    [Fact] public async Task Lti_launch_is_exempt() => Assert.NotEqual(HttpStatusCode.Forbidden, await Post("/lti/launch", "cross-site"));
}
