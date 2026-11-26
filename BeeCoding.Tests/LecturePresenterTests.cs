using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class LecturePresenterTests
{
    private const char Sep = '\u001e';

    private sealed class Teacher(WebSocket ws)
    {
        private int _id;
        public async Task<bool> Push(int boardId, string code, bool takeOver = false)
        {
            var id = (++_id).ToString();
            await Send(ws, JsonSerializer.Serialize(new { type = 1, invocationId = id, target = "PushLecture", arguments = new object[] { boardId, 0, code, "c", "", takeOver } }));
            var seen = new StringBuilder(); var buf = new byte[16 * 1024];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                foreach (var msg in seen.ToString().Split(Sep, StringSplitOptions.RemoveEmptyEntries))
                    if (msg.Contains($"\"invocationId\":\"{id}\"") && msg.EndsWith('}'))
                        return JsonDocument.Parse(msg).RootElement.GetProperty("result").GetProperty("accepted").GetBoolean();
                var r = await ws.ReceiveAsync(buf, cts.Token);
                seen.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            }
        }
        public Task Leave() => ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }

    private static Task Send(WebSocket ws, string json) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json + Sep), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task The_presenter_keeps_the_slot_until_they_leave_or_someone_takes_over_on_purpose()
    {
        using var app = new TestApp();
        async Task<(HttpClient Http, string Cookie)> SignIn(string path, object body)
        {
            var http = app.CreateClient();
            var r = await http.PostAsJsonAsync(path, body);
            r.EnsureSuccessStatusCode();
            return (http, string.Join("; ", r.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        }
        var first = await SignIn("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
        var second = await SignIn("/api/auth/register", new { email = "co@t.test", password = "password1!", displayName = "Co Teacher", role = "Student" });
        int boardId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            boardId = await db.Boards.Where(b => b.JoinCode == "DEMO01").Select(b => b.Id).FirstAsync();
            var co = db.Users.Single(u => u.Email == "co@t.test");
            db.BoardMemberships.Add(new BoardMembership { BoardId = boardId, UserId = co.Id, Role = MembershipRole.Teacher });
            await db.SaveChangesAsync();
        }
        (await first.Http.PatchAsJsonAsync("/api/boards/demo-board", new { lecturingMode = true })).EnsureSuccessStatusCode();

        async Task<Teacher> Connect(string cookie)
        {
            var wsc = app.Server.CreateWebSocketClient();
            wsc.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
            var ws = await wsc.ConnectAsync(new Uri("ws://localhost/hubs/board"), CancellationToken.None);
            await Send(ws, "{\"protocol\":\"json\",\"version\":1}");
            await ws.ReceiveAsync(new byte[1024], CancellationToken.None);
            return new Teacher(ws);
        }
        var a = await Connect(first.Cookie);
        var b = await Connect(second.Cookie);

        Assert.True(await a.Push(boardId, "a1"));
        Assert.False(await b.Push(boardId, "b1"));                  // someone is presenting
        Assert.True(await b.Push(boardId, "b2", takeOver: true));   // on purpose
        Assert.False(await a.Push(boardId, "a2"));

        await b.Leave();                                             // the presenter's page goes: the slot frees
        var accepted = false;
        for (var i = 0; i < 50 && !accepted; i++) { accepted = await a.Push(boardId, "a3"); if (!accepted) await Task.Delay(100); }
        Assert.True(accepted);
    }
}
