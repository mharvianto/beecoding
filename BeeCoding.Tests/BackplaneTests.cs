using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class BackplaneTests
{
    /// <summary>Two web instances on one Redis: a board event raised on the second reaches a browser connected to the first.
    /// Runs when <c>BEECODING_TEST_REDIS</c> is set (host:port).</summary>
    [Fact]
    public async Task A_board_event_raised_on_one_instance_reaches_a_browser_on_another()
    {
        var redis = Environment.GetEnvironmentVariable("BEECODING_TEST_REDIS");
        if (string.IsNullOrEmpty(redis)) return;
        Environment.SetEnvironmentVariable("Realtime__Backend", "redis");
        Environment.SetEnvironmentVariable("Realtime__RedisConnectionString", redis);
        Environment.SetEnvironmentVariable("Realtime__KeyPrefix", "bp" + Guid.NewGuid().ToString("N")[..8] + ":");
        try
        {
            using var a = new TestApp();
            var http = a.CreateClient();
            var login = await http.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
            login.EnsureSuccessStatusCode();
            var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
            int boardId;
            using (var scope = a.Services.CreateScope())
                boardId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Boards.Where(b => b.JoinCode == "DEMO01").Select(b => b.Id).FirstAsync();

            using var b = new TestApp();   // the second instance, its own database, the same Redis
            b.CreateClient();

            var wsc = a.Server.CreateWebSocketClient();
            wsc.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
            using var ws = await wsc.ConnectAsync(new Uri("ws://localhost/hubs/board"), CancellationToken.None);
            const char Sep = '\u001e';
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
            await ReadUntil(s => s.Contains(Sep));
            await Send("{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinBoard\",\"arguments\":[" + boardId + "]}");
            Assert.Contains("\"type\":3", await ReadUntil(s => s.Contains("\"invocationId\":\"1\"")));

            await Task.Delay(500);   // the other instance must have subscribed to the group channel
            await b.Services.GetRequiredService<IBoardNotifier>().ProblemChangedAsync(boardId);

            Assert.Contains("problemChanged", await ReadUntil(s => s.Contains("problemChanged") || s.Contains("<closed>")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Realtime__Backend", null);
            Environment.SetEnvironmentVariable("Realtime__RedisConnectionString", null);
            Environment.SetEnvironmentVariable("Realtime__KeyPrefix", null);
        }
    }
}
