extern alias lspnode;
using System.Net;
using System.Net.WebSockets;
using lspnode::BeeCoding.Lsp;
using BeeCoding.Services.Lsp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The clangd node's front door: without the shared secret nobody may run clangd there.</summary>
public class LspNodeTests
{
    private static async Task<(WebApplication App, string Base)> StartAsync()
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Services.Configure<LspOptions>(_ => { });
        b.Services.AddSingleton<SharedClangd>();
        b.Services.AddSingleton<LspBridge>();
        var app = b.Build();
        app.UseWebSockets();
        app.MapLspNode("node-secret");
        await app.StartAsync();
        return (app, app.Urls.First());
    }

    [Fact]
    public async Task Only_the_right_secret_gets_in_and_a_user_is_required()
    {
        var (app, url) = await StartAsync();
        await using var _ = app;
        using var http = new HttpClient();
        async Task<HttpStatusCode> Get(string? auth, string query)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url + "/lsp/node" + query);
            if (auth is not null) req.Headers.TryAddWithoutValidation("Authorization", auth);
            return (await http.SendAsync(req)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(null, "?user=1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get("Bearer nope", "?user=1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get("node-secret", "?user=1"));           // the scheme matters too
        Assert.Equal(HttpStatusCode.BadRequest, await Get("Bearer node-secret", "?user=1"));       // right secret, but not a WebSocket

        // right secret, a real WebSocket request, but no user: refused before any clangd starts
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer node-secret");
        await Assert.ThrowsAsync<WebSocketException>(() => ws.ConnectAsync(new Uri(url.Replace("http", "ws") + "/lsp/node"), CancellationToken.None));
        using var bad = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(() => bad.ConnectAsync(new Uri(url.Replace("http", "ws") + "/lsp/node?user=1"), CancellationToken.None));   // no secret at all
    }
}
