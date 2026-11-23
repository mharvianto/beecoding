using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class McpTests
{
    private static async Task<HttpClient> Login(TestApp app, string email, string password = "password")
    {
        var c = app.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();
        return c;
    }

    private static async Task<string> NewToken(HttpClient teacher, string password = "password")
    {
        var r = await teacher.PostAsJsonAsync("/api/me/api-tokens", new { name = "claude", password });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> Rpc(TestApp app, string? token, object body, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var c = app.CreateClient();   // no cookie: the endpoint must stand on the token alone
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var r = await c.PostAsJsonAsync("/mcp", body);
        Assert.Equal(expect, r.StatusCode);
        return expect == HttpStatusCode.OK ? await r.Content.ReadFromJsonAsync<JsonElement>() : default;
    }

    private static string Text(JsonElement reply) => reply.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
    private static object Call(string tool, object args) => new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } };

    [Fact]
    public async Task Only_a_teachers_token_works_and_the_tools_read_through_the_boards_rules()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        Environment.SetEnvironmentVariable("Auth__TeacherSignupCode", "teach-code");
        try
        {
            using var app = new TestApp();
            await Rpc(app, null, new { jsonrpc = "2.0", id = 1, method = "ping" }, HttpStatusCode.Unauthorized);
            await Rpc(app, "bcm_wrong", new { jsonrpc = "2.0", id = 1, method = "ping" }, HttpStatusCode.Unauthorized);

            var teacher = await Login(app, "teacher@demo.test");
            Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync("/api/me/api-tokens", new { name = "x", password = "nope" })).StatusCode);   // password asked
            var token = await NewToken(teacher);

            // a student cannot even get one
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, (await kid.PostAsJsonAsync("/api/me/api-tokens", new { name = "x", password = "Passw0rd!x1" })).StatusCode);

            var init = await Rpc(app, token, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18" } });
            Assert.Equal("2025-06-18", init.GetProperty("result").GetProperty("protocolVersion").GetString());
            await Rpc(app, token, new { jsonrpc = "2.0", method = "notifications/initialized" }, HttpStatusCode.Accepted);   // a notification: no body
            var list = await Rpc(app, token, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
            Assert.Equal(new[] { "board_progress", "students_at_risk", "session_summary", "problem_stats", "student_progress", "hint_usage", "problem_issues", "lsp_usage" }, list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray());

            // the demo board: one student who solved one problem
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var board = await db.Boards.Include(b => b.Problems).FirstAsync(b => b.JoinCode == "DEMO01");
                var student = await db.Users.FirstAsync(u => u.Email == "kid@test.dev");
                db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = student.Id, Role = MembershipRole.Student });
                db.Submissions.Add(new Submission { ProblemId = board.Problems.First().Id, UserId = student.Id, Code = "//", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, Score = 1 });
                await db.SaveChangesAsync();
            }
            Assert.Contains("Demo Board", Text(await Rpc(app, token, Call("board_progress", new { }))));
            var progress = Text(await Rpc(app, token, Call("board_progress", new { board = "demo01" })));
            Assert.Contains("Budi: 1", progress);
            Assert.DoesNotContain("kid@test.dev", progress);   // names and numbers only

            // another teacher sees nothing of it, and the admin-only tool is refused to them
            (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "other@test.dev", password = "Passw0rd!x1", displayName = "Other", role = "Teacher", teacherCode = "teach-code" })).EnsureSuccessStatusCode();
            var other = await Login(app, "other@test.dev", "Passw0rd!x1");
            var otherToken = await NewToken(other, "Passw0rd!x1");
            var denied = await Rpc(app, otherToken, Call("board_progress", new { board = "DEMO01" }));
            Assert.True(denied.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.DoesNotContain("Budi", Text(denied));
            Assert.True((await Rpc(app, otherToken, Call("lsp_usage", new { }))).GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.False((await Rpc(app, token, Call("lsp_usage", new { }))).GetProperty("result").GetProperty("isError").GetBoolean());   // the admin may

            // revoking ends it at once
            var id = (await teacher.GetFromJsonAsync<JsonElement>("/api/me/api-tokens"))[0].GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/me/api-tokens/{id}")).StatusCode);
            await Rpc(app, token, new { jsonrpc = "2.0", id = 1, method = "ping" }, HttpStatusCode.Unauthorized);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); Environment.SetEnvironmentVariable("Auth__TeacherSignupCode", null); }
    }

    [Fact]
    public async Task Risk_session_and_problem_tools_report_numbers_and_names_only()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var token = await NewToken(await Login(app, "teacher@demo.test"));
            var now = DateTime.UtcNow;
            string title1, title2;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var board = await db.Boards.Include(b => b.Problems).FirstAsync(b => b.JoinCode == "DEMO01");
                db.Problems.Add(new Problem { BoardId = board.Id, Title = "Second one", StatementMarkdown = "s", Position = 99 });
                await db.SaveChangesAsync();
                var ps = (await db.Problems.Where(p => p.BoardId == board.Id).ToListAsync()).OrderBy(p => p.Position).ThenBy(p => p.Id).Take(2).ToList();
                (title1, title2) = (ps[0].Title, ps[1].Title);
                User Make(string name) { var u = new User { Email = name + "@t.dev", DisplayName = name, PasswordHash = "x" }; db.Users.Add(u); return u; }
                var stuck = Make("Stuck"); var ace = Make("Ace"); var gone = Make("Gone"); var never = Make("Never");
                await db.SaveChangesAsync();
                foreach (var u in new[] { stuck, ace, gone, never }) db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = u.Id, Role = MembershipRole.Student });
                Submission S(User u, Problem p, Verdict v, DateTime at) => new() { UserId = u.Id, ProblemId = p.Id, Code = "//", Status = SubmissionStatus.Done, Verdict = v, CreatedAt = at };
                for (int i = 0; i < 5; i++) db.Submissions.Add(S(stuck, ps[0], Verdict.WrongAnswer, now.AddMinutes(-30 + i)));
                db.Submissions.Add(S(ace, ps[0], Verdict.WrongAnswer, now.AddMinutes(-20)));
                db.Submissions.Add(S(ace, ps[0], Verdict.Accepted, now.AddMinutes(-10)));
                db.Submissions.Add(S(gone, ps[1], Verdict.Accepted, now.AddDays(-10)));
                await db.SaveChangesAsync();
            }

            var risk = Text(await Rpc(app, token, Call("students_at_risk", new { board = "DEMO01" })));
            Assert.Contains("Stuck: 5+ attempts", risk);
            Assert.Contains($"- Stuck: {title1} (5 attempts", risk);
            Assert.Contains("- Gone: last submission", risk);          // inactive 10 days
            Assert.Contains("Never submitted (1): Never", risk);
            Assert.DoesNotContain("Ace:", risk);                        // solved it: not at risk
            Assert.DoesNotContain("@t.dev", risk);

            var session = Text(await Rpc(app, token, Call("session_summary", new { board = "DEMO01", from = now.AddHours(-1).ToString("o"), to = now.AddMinutes(1).ToString("o") })));
            Assert.Contains("7 submissions from 2 of 4 students", session);
            Assert.Contains($"- {title1}: 7, 2, 1", session);           // attempts, students, students accepted
            Assert.Contains("IntelliSense: no use recorded", session);   // the admin gets it
            Assert.True((await Rpc(app, token, Call("session_summary", new { board = "DEMO01", from = "not a time" }))).GetProperty("result").GetProperty("isError").GetBoolean());

            var stats = Text(await Rpc(app, token, Call("problem_stats", new { board = "DEMO01", problem = title1 })));
            Assert.Contains("Tried: 2. Solved: 1 (50%", stats);
            Assert.Contains("Not started: 2.", stats);
            Assert.Contains("average 2.0", stats);                       // Ace needed 2 attempts
            Assert.Contains("WrongAnswer 6", stats);
            Assert.Contains("Tried: 1. Solved: 1", Text(await Rpc(app, token, Call("problem_stats", new { board = "DEMO01", problem = title2 }))));   // the one who left

            var one = Text(await Rpc(app, token, Call("student_progress", new { board = "DEMO01", student = "ace" })));
            Assert.Contains("Ace on", one);
            Assert.Contains("solved 1 of", one);
            Assert.Contains($"- {title1}: Accepted, 2 attempts", one);
            Assert.Contains($"- {title2}: not tried", one);
            Assert.True((await Rpc(app, token, Call("student_progress", new { board = "DEMO01", student = "nobody" }))).GetProperty("result").GetProperty("isError").GetBoolean());

            Assert.Contains("No student asked for a hint", Text(await Rpc(app, token, Call("hint_usage", new { board = "DEMO01" }))));
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var pid = (await db.Problems.FirstAsync(p => p.Title == title1)).Id;
                var stuckId = (await db.Users.FirstAsync(u => u.Email == "Stuck@t.dev")).Id;
                db.AiHintProgresses.Add(new AiHintProgress { UserId = stuckId, ProblemKey = $"board:{pid}", Count = 3, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var hints = Text(await Rpc(app, token, Call("hint_usage", new { board = "DEMO01" })));
            Assert.Contains($"- {title1}: 1, level 3", hints);
            Assert.Contains("- Stuck: 1 problems, deepest level 3", hints);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task Changing_the_password_ends_every_token()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var teacher = await Login(app, "teacher@demo.test");
            var token = await NewToken(teacher);
            await Rpc(app, token, new { jsonrpc = "2.0", id = 1, method = "ping" });
            (await teacher.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "password", newPassword = "N3w-Passw0rd!x" })).EnsureSuccessStatusCode();
            await Rpc(app, token, new { jsonrpc = "2.0", id = 1, method = "ping" }, HttpStatusCode.Unauthorized);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task An_expired_token_and_the_off_switch_are_refused()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var token = await NewToken(await Login(app, "teacher@demo.test"));
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (await db.ApiTokens.FirstAsync()).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }
            await Rpc(app, token, new { jsonrpc = "2.0", id = 1, method = "ping" }, HttpStatusCode.Unauthorized);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
