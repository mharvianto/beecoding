using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SessionAndHubTests
{
    [Fact]
    public async Task Changing_the_password_ends_every_other_session_but_not_the_current_one()
    {
        using var app = new TestApp();
        var a = app.CreateClient();
        (await a.PostAsJsonAsync("/api/auth/register", new { email = "pw@test.dev", password = "Passw0rd!x1", displayName = "Pw", role = "Student" })).EnsureSuccessStatusCode();
        var b = app.CreateClient();
        (await b.PostAsJsonAsync("/api/auth/login", new { email = "pw@test.dev", password = "Passw0rd!x1" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/auth/me")).StatusCode);

        (await b.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Passw0rd!x1", newPassword = "NewPassw0rd!y2" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/auth/me")).StatusCode);   // the other session (a stolen copy, say) is gone
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/auth/me")).StatusCode);              // the one that changed it carries on
    }

    [Fact]
    public void Simultaneous_attempts_cannot_get_past_the_cap()
    {
        var t = new LoginThrottle();
        var allowed = 0;
        Parallel.For(0, 1000, _ => { if (t.TryBegin("1.1.1.1", "victim@x")) Interlocked.Increment(ref allowed); });
        Assert.Equal(10, allowed);                                    // the per-(account, IP) cap, not "however many arrived together"
        Assert.False(t.TryBegin("1.1.1.1", "victim@x"));
        Assert.True(t.TryBegin("2.2.2.2", "victim@x"));              // the real owner elsewhere is not locked out
        t.RecordSuccess("1.1.1.1", "victim@x");
        Assert.True(t.TryBegin("1.1.1.1", "victim@x"));              // a success clears it
    }

    [Fact]
    public async Task Only_members_can_read_a_boards_live_lecture_buffer()
    {
        using var app = new TestApp();
        var teacher = app.CreateClient();
        var login = await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
        login.EnsureSuccessStatusCode();
        var outsider = app.CreateClient();
        var reg = await outsider.PostAsJsonAsync("/api/auth/register", new { email = "outsider@test.dev", password = "Passw0rd!x1", displayName = "Outsider", role = "Student" });
        reg.EnsureSuccessStatusCode();

        int boardId;
        using (var scope = app.Services.CreateScope())
        {
            boardId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Boards.Where(b => b.JoinCode == "DEMO01").Select(b => b.Id).FirstAsync();
            await scope.ServiceProvider.GetRequiredService<ILectureStore>().SetAsync(boardId, 0, "SECRETCODE", "cpp", "Teacher", "", 1);
        }

        Assert.Contains("SECRETCODE", await GetLectureAsync(app, login, boardId));
        Assert.DoesNotContain("SECRETCODE", await GetLectureAsync(app, reg, boardId));
    }

    private static async Task<string> GetLectureAsync(TestApp app, HttpResponseMessage signedIn, int boardId)
    {
        var cookie = string.Join("; ", signedIn.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        var wsc = app.Server.CreateWebSocketClient();
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
        await Send("{\"type\":1,\"invocationId\":\"5\",\"target\":\"GetLecture\",\"arguments\":[" + boardId + ",0]}");
        return await ReadUntil(s => s.Contains("\"invocationId\":\"5\"") || s.Contains("<closed>"));
    }
}
