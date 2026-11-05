using System.Net.Http.Json;
using System.Net.WebSockets;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class WebSocketOriginTests
{
    private static async Task<(TestApp app, string cookie)> SignedInAsync()
    {
        var app = new TestApp();
        var login = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
        login.EnsureSuccessStatusCode();
        return (app, string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
    }

    private static async Task<WebSocket> Connect(TestApp app, string cookie, string path, string? secFetchSite)
    {
        var wsc = app.Server.CreateWebSocketClient();
        wsc.ConfigureRequest = r =>
        {
            r.Headers["Cookie"] = cookie;
            if (secFetchSite is not null) r.Headers["Sec-Fetch-Site"] = secFetchSite;
        };
        return await wsc.ConnectAsync(new Uri("ws://localhost" + path), CancellationToken.None);
    }

    [Theory]
    [InlineData("/hubs/board")]
    [InlineData("/lsp/cpp")]
    public async Task A_websocket_opened_from_another_site_is_refused_while_the_apps_own_page_and_scripts_are_not(string path)
    {
        var (app, cookie) = await SignedInAsync();
        using var _ = app;
        // a page on another site: the browser labels the handshake cross-site, and it must not ride the visitor's cookie
        await Assert.ThrowsAnyAsync<Exception>(() => Connect(app, cookie, path, "cross-site"));
        // the app's own page (same-origin) and a non-browser client (no label) get through the gate: whatever answers is not this 403
        foreach (var label in new string?[] { "same-origin", null })
        {
            try { using var ws = await Connect(app, cookie, path, label); }
            catch (Exception ex) { Assert.DoesNotContain("403", ex.ToString()); }
        }
    }
}
