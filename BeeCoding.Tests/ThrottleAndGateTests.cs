using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class ThrottleAndGateTests
{
    [Fact]
    public void A_burst_of_parallel_requests_gets_exactly_the_cap()
    {
        var t = new ResetRequestThrottle();
        var allowed = 0;
        Parallel.For(0, 400, _ => { if (t.TryAcquire("mail:victim@test.dev", 3)) Interlocked.Increment(ref allowed); });
        Assert.Equal(3, allowed);
    }

    [Fact]
    public async Task Run_does_not_apply_or_reveal_the_rules_of_a_hidden_problem_to_a_student()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "probe@test.dev", password = "Passw0rd!x1", displayName = "Probe", role = "Student" })).EnsureSuccessStatusCode();
        (await kid.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
        int hiddenId, openId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
            var hidden = new Problem { BoardId = board.Id, Title = "H", StatementMarkdown = "x", Hidden = true, BannedHeaders = "algorithm" };
            var open = new Problem { BoardId = board.Id, Title = "O", StatementMarkdown = "x", BannedHeaders = "algorithm" };
            db.Problems.AddRange(hidden, open); await db.SaveChangesAsync();
            hiddenId = hidden.Id; openId = open.Id;
        }
        async Task<string> RunOn(int id) =>
            await (await kid.PostAsJsonAsync("/api/run", new { language = "cpp", code = "#include <algorithm>\nint main(){}", stdin = "", problemId = id })).Content.ReadAsStringAsync();

        Assert.Contains("does not allow", await RunOn(openId));                       // a visible problem keeps enforcing its rules
        await Task.Delay(1700);                                                       // the run spacing
        Assert.DoesNotContain("does not allow", await RunOn(hiddenId));               // a hidden one is not probed through them
    }

    [Fact]
    public async Task A_post_from_another_origin_is_refused_when_the_browser_sends_no_fetch_metadata()
    {
        using var app = new TestApp();
        async Task<HttpStatusCode> Post(string? origin)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            if (origin is not null) req.Headers.Add("Origin", origin);
            return (await app.CreateClient().SendAsync(req)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Forbidden, await Post("https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, await Post("null"));
        Assert.NotEqual(HttpStatusCode.Forbidden, await Post("http://localhost"));   // its own origin
        Assert.NotEqual(HttpStatusCode.Forbidden, await Post(null));                 // a script
    }

    [Fact]
    public async Task Two_sign_ups_with_one_address_at_once_never_end_in_a_server_error()
    {
        using var app = new TestApp();
        var codes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "twin@test.dev", password = "Passw0rd!x1", displayName = "Twin", role = "Student" })).StatusCode));
        Assert.Single(codes, HttpStatusCode.OK);
        Assert.Single(codes, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_teacher_needs_a_problem_of_their_own_to_answer_the_survey_and_an_admin_does_not()
    {
        Environment.SetEnvironmentVariable("Auth__TeacherSignupCode", "teach-code");
        try
        {
            using (var app = new TestApp())
            {
                var t = app.CreateClient();
                (await t.PostAsJsonAsync("/api/auth/register", new { email = "newteacher@test.dev", password = "Passw0rd!x1", displayName = "New", role = "Teacher", teacherCode = "teach-code" })).EnsureSuccessStatusCode();
                Assert.Equal(HttpStatusCode.Forbidden, (await t.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9" })).StatusCode);
            }
            Environment.SetEnvironmentVariable("Admin__Emails", "newteacher@test.dev");
            using (var app = new TestApp())
            {
                var t = app.CreateClient();
                (await t.PostAsJsonAsync("/api/auth/register", new { email = "newteacher@test.dev", password = "Passw0rd!x1", displayName = "New", role = "Teacher", teacherCode = "teach-code" })).EnsureSuccessStatusCode();
                (await t.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9" })).EnsureSuccessStatusCode();
            }
        }
        finally { Environment.SetEnvironmentVariable("Auth__TeacherSignupCode", null); Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
