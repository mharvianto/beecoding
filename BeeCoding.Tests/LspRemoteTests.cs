using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using BeeCoding.Services.Lsp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The web app relaying the editor to a clangd node. The node here is a stand-in (no clangd needed): it checks the secret,
/// says "ready" and echoes every frame back with a prefix.</summary>
[Collection("app")]
public class LspRemoteTests
{
    [Fact]
    public void A_student_always_lands_on_the_same_node_and_the_others_are_tried_in_turn()
    {
        string[] urls = ["ws://a", "ws://b", "ws://c"];
        var order = LspBridge.Candidates(urls, "42").ToList();
        Assert.Equal(order, LspBridge.Candidates(urls, "42"));          // the same on every call (and so on every web instance)
        Assert.Equal(urls.OrderBy(x => x), order.OrderBy(x => x));      // all of them are candidates, once
        Assert.Contains(Enumerable.Range(1, 60).Select(i => LspBridge.Candidates(urls, i.ToString()).First()).Distinct(), x => true);
        Assert.True(Enumerable.Range(1, 60).Select(i => LspBridge.Candidates(urls, i.ToString()).First()).Distinct().Count() == 3);   // students spread over the nodes
        Assert.Single(LspBridge.Candidates(["ws://only"], "7"));
    }

    private sealed class FakeNode : IAsyncDisposable
    {
        public readonly List<string> Seen = [];
        public string? Auth, Query;
        public readonly TaskCompletionSource Closed = new();
        private readonly WebApplication _app;
        public string Url { get; }

        public FakeNode(string token)
        {
            var b = WebApplication.CreateBuilder();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            _app = b.Build();
            _app.UseWebSockets();
            _app.Map("/lsp/node", async ctx =>
            {
                Auth = ctx.Request.Headers.Authorization;
                Query = ctx.Request.QueryString.Value;
                if (Auth != "Bearer " + token) { ctx.Response.StatusCode = 401; return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await ws.SendAsync(Encoding.UTF8.GetBytes("{\"beecoding\":\"ready\",\"uri\":\"file:///node/main.cpp\"}"), WebSocketMessageType.Text, true, default);
                var buf = new byte[8192];
                while (true)
                {
                    var r = await ws.ReceiveAsync(buf, default);
                    if (r.MessageType == WebSocketMessageType.Close) { Closed.TrySetResult(); return; }
                    var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                    lock (Seen) Seen.Add(text);
                    await ws.SendAsync(Encoding.UTF8.GetBytes("echo:" + text), WebSocketMessageType.Text, true, default);
                }
            });
            _app.Start();
            Url = _app.Urls.First().Replace("http://", "ws://") + "/lsp/node";
        }

        public async ValueTask DisposeAsync() { await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    private static async Task<string> ReadAsync(WebSocket ws)
    {
        var buf = new byte[8192];
        var r = await ws.ReceiveAsync(buf, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        return r.MessageType == WebSocketMessageType.Close ? "<closed:" + r.CloseStatusDescription + ">" : Encoding.UTF8.GetString(buf, 0, r.Count);
    }

    private static async Task<WebSocket> ConnectAsync(TestApp app, string email)
    {
        var reg = await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x1", displayName = "Lsp", role = "Student" });
        reg.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", reg.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        var wsc = app.Server.CreateWebSocketClient();
        wsc.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
        return await wsc.ConnectAsync(new Uri("ws://localhost/lsp/cpp?lang=cpp&style=Google"), CancellationToken.None);
    }

    private static void Config(string? url0, string? url1, string token)
    {
        Environment.SetEnvironmentVariable("Lsp__Enabled", "true");
        Environment.SetEnvironmentVariable("Lsp__RemoteUrls__0", url0);
        Environment.SetEnvironmentVariable("Lsp__RemoteUrls__1", url1);
        Environment.SetEnvironmentVariable("Lsp__RemoteToken", token);
    }

    [Fact]
    public async Task The_web_app_relays_the_editor_to_a_node_with_the_secret_and_skips_a_node_that_is_down()
    {
        await using var node = new FakeNode("s3cret");
        Config("ws://127.0.0.1:1/lsp/node", node.Url, "s3cret");   // the first one is down: whichever the student hashes to, the live one answers
        try
        {
            using var app = new TestApp();
            using var ws = await ConnectAsync(app, "relay1@test.dev");

            Assert.Contains("\"ready\"", await ReadAsync(ws));                       // the node's hello arrives untouched
            await ws.SendAsync(Encoding.UTF8.GetBytes("{\"method\":\"initialize\"}"), WebSocketMessageType.Text, true, default);
            Assert.Equal("echo:{\"method\":\"initialize\"}", await ReadAsync(ws));   // and a frame goes there and back

            Assert.Equal("Bearer s3cret", node.Auth);
            Assert.Contains("lang=cpp", node.Query); Assert.Contains("style=Google", node.Query); Assert.Contains("user=", node.Query);
            Assert.Contains("shared=0", node.Query);                                 // the Admin > System choice travels along (off by default)

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", default);
            await node.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));              // closing the editor closes the node's session
        }
        finally { Config(null, null, ""); Environment.SetEnvironmentVariable("Lsp__Enabled", null); }
    }

    [Fact]
    public async Task With_no_node_reachable_the_editor_is_told_the_server_is_busy()
    {
        Config("ws://127.0.0.1:1/lsp/node", null, "x");
        try
        {
            using var app = new TestApp();
            using var ws = await ConnectAsync(app, "relay2@test.dev");
            Assert.Equal("<closed:server busy>", await ReadAsync(ws));               // basic completion stays, as before when clangd was full
        }
        finally { Config(null, null, ""); Environment.SetEnvironmentVariable("Lsp__Enabled", null); }
    }

    [Fact]
    public async Task A_node_that_refuses_the_secret_is_treated_as_down()
    {
        await using var node = new FakeNode("right");
        Config(node.Url, null, "wrong");
        try
        {
            using var app = new TestApp();
            using var ws = await ConnectAsync(app, "relay3@test.dev");
            Assert.Equal("<closed:server busy>", await ReadAsync(ws));
            Assert.Equal("Bearer wrong", node.Auth);
        }
        finally { Config(null, null, ""); Environment.SetEnvironmentVariable("Lsp__Enabled", null); }
    }
}
