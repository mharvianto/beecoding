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
            Assert.Equal(new[] { "board_progress", "problem_issues", "lsp_usage" }, list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray());

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
