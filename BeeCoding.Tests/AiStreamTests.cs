using System.Net;
using System.Text;
using BeeCoding.Services.Ai;
using Xunit;

namespace BeeCoding.Tests;

public class AiStreamTests
{
    private sealed class Fake(string sse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });
    }

    private static async Task<List<string>> Run(string sse)
    {
        var rt = new AiProviderRuntime();
        var svc = new AiTutorService(new HttpClient(new Fake(sse)), Microsoft.Extensions.Options.Options.Create(new AiOptions { ApiKey = "k", BaseUrl = "https://x.example/v1" }),
            new AiRuntimeSettings(), rt, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiTutorService>.Instance);
        var ctx = new AiHintContext("s", "cpp", "int main(){}", null, null, null, null, null, []);
        var got = new List<string>();
        await foreach (var c in svc.StreamAsync(ctx, "id", 1, default)) got.Add(c);
        return got;
    }

    private static string Chunk(string field, string text) => $"data: {{\"choices\":[{{\"delta\":{{\"{field}\":\"{text}\"}}}}]}}\n\n";

    [Fact]
    public async Task A_reply_written_only_as_reasoning_content_is_still_shown()
    {
        var got = await Run(Chunk("reasoning_content", "Coba pakai ") + Chunk("reasoning_content", "operator +") + "data: [DONE]\n\n");
        Assert.Contains(AiTutorService.FinalSentinel + "Coba pakai operator +", got);
    }

    [Fact]
    public async Task Content_wins_over_reasoning()
    {
        var got = await Run(Chunk("reasoning_content", "mikir") + Chunk("content", "Hint") + "data: [DONE]\n\n");
        Assert.Contains(AiTutorService.FinalSentinel + "Hint", got);
    }

    [Fact]
    public async Task An_empty_reply_is_an_error_not_silence()
    {
        await Assert.ThrowsAsync<AiUnavailableException>(() => Run("data: [DONE]\n\n"));
    }
}
