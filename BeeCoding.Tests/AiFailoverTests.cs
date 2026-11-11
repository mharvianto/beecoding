using System.Net;
using System.Text;
using BeeCoding.Services.Ai;
using Xunit;

namespace BeeCoding.Tests;

public class AiFailoverTests
{
    private sealed class Route(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Hosts.Add(r.RequestUri!.Host); return Task.FromResult(f(r)); }
    }

    private static HttpResponseMessage Status(HttpStatusCode c) => new(c) { Content = new StringContent("no") };
    private static HttpResponseMessage Sse(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent($"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{text}\"}}}}]}}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent($"{{\"choices\":[{{\"message\":{{\"content\":\"{text}\"}}}}]}}", Encoding.UTF8, "application/json") };

    private static AiTutorService Svc(Route h, AiProviderRuntime? rt = null) => new(new HttpClient(h),
        Microsoft.Extensions.Options.Options.Create(new AiOptions
        {
            ApiKey = "main", BaseUrl = "https://main.example/v1",
            Fallbacks = [new() { ApiKey = "b", BaseUrl = "https://backup.example/v1", Model = "m2" }],
        }),
        new AiRuntimeSettings(), rt ?? new AiProviderRuntime(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AiTutorService>.Instance);

    private static readonly AiHintContext Ctx = new("s", "cpp", "int main(){}", null, null, null, null, null, []);

    private static async Task<List<string>> Stream(AiTutorService s)
    {
        var got = new List<string>();
        await foreach (var c in s.StreamAsync(Ctx, "id", 1, default)) got.Add(c);
        return got;
    }

    [Fact]
    public async Task Stream_moves_to_the_backup_when_the_main_provider_errors()
    {
        var h = new Route(r => r.RequestUri!.Host == "main.example" ? Status(HttpStatusCode.TooManyRequests) : Sse("Hint"));
        var got = await Stream(Svc(h));
        Assert.Contains(AiTutorService.FinalSentinel + "Hint", got);
        Assert.Equal(["main.example", "backup.example"], h.Hosts);
    }

    [Fact]
    public async Task A_bad_request_is_not_sent_to_the_backup()
    {
        var h = new Route(_ => Status(HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<AiUnavailableException>(() => Stream(Svc(h)));
        Assert.Equal(["main.example"], h.Hosts);
    }

    [Fact]
    public async Task Hint_moves_to_the_backup_on_a_403()
    {
        var h = new Route(r => r.RequestUri!.Host == "main.example" ? Status(HttpStatusCode.Forbidden) : Json("Coba lagi"));
        var res = await Svc(h).HintAsync(Ctx, "id", 1, default);
        Assert.Equal("Coba lagi", res.Text);
    }

    [Fact]
    public async Task All_providers_failing_still_reports_the_error()
    {
        var h = new Route(_ => Status(HttpStatusCode.BadGateway));
        await Assert.ThrowsAsync<AiUnavailableException>(() => Svc(h).HintAsync(Ctx, "id", 1, default));
        Assert.Equal(["main.example", "backup.example"], h.Hosts);
    }

    [Fact]
    public async Task An_organization_with_its_own_key_never_falls_back_to_the_platform()
    {
        var rt = new AiProviderRuntime();
        rt.SetOrg(1, "org-key", null, null, null);
        var h = new Route(_ => Status(HttpStatusCode.BadGateway));
        var s = Svc(h, rt);
        s.UseOrganization(1);
        await Assert.ThrowsAsync<AiUnavailableException>(() => s.HintAsync(Ctx, "id", 1, default));
        Assert.Single(h.Hosts);
    }
}
