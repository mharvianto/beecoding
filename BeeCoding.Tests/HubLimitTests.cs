using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class HubLimitTests
{
    [Fact]
    public async Task A_large_draft_is_accepted_by_the_hub_instead_of_closing_the_connection()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
        login.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        int boardId;
        using (var scope = app.Services.CreateScope())
            boardId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Boards.Where(b => b.JoinCode == "DEMO01").Select(b => b.Id).FirstAsync();

        var wsc = app.Server.CreateWebSocketClient();
        wsc.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
        using var ws = await wsc.ConnectAsync(new Uri("ws://localhost/hubs/board"), CancellationToken.None);   // straight to the socket, no negotiate

        const char Sep = '\u001e';   // SignalR's JSON protocol ends every message with this
        async Task Send(string json) => await ws.SendAsync(Encoding.UTF8.GetBytes(json + Sep), WebSocketMessageType.Text, true, CancellationToken.None);
        async Task<string> ReadUntil(Func<string, bool> done)
        {
            var seen = new StringBuilder(); var buf = new byte[16 * 1024];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!done(seen.ToString()))
            {
                var r = await ws.ReceiveAsync(buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return seen + "<closed>";
                seen.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            }
            return seen.ToString();
        }

        await Send("{\"protocol\":\"json\",\"version\":1}");
        Assert.StartsWith("{}", await ReadUntil(s => s.Contains(Sep)));

        var code = "int main(){ /* " + new string('x', 100_000) + " */ }";   // ~100 KB: over SignalR's 32 KB default, under our own 200 KB cap
        await Send("{\"type\":1,\"invocationId\":\"7\",\"target\":\"PushDraft\",\"arguments\":[" + boardId + ",1," + System.Text.Json.JsonSerializer.Serialize(code) + "]}");
        var reply = await ReadUntil(s => s.Contains("\"invocationId\":\"7\"") || s.Contains("<closed>"));
        Assert.DoesNotContain("<closed>", reply);
        Assert.Contains("\"type\":3", reply);   // a completion: the hub took the message
    }
}
