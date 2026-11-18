using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SurveyTests
{
    [Fact]
    public async Task A_user_keeps_one_editable_response_and_only_an_admin_reads_everyones_without_identity()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Kid", role = "Student" })).EnsureSuccessStatusCode();

            // a student has to have used the judge first
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9" })).StatusCode);
            var st = await kid.GetFromJsonAsync<JsonElement>("/api/survey/status");
            Assert.False(st.GetProperty("eligible").GetBoolean());
            Assert.Equal(3, st.GetProperty("need").GetInt32());
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var kidId = (await db.Users.FirstAsync(u => u.Email == "kid@test.dev")).Id;
                var problemId = (await db.Problems.FirstAsync()).Id;
                for (var i = 0; i < 2; i++) db.Submissions.Add(new Submission { ProblemId = problemId, UserId = kidId, Code = "x", Status = SubmissionStatus.Done });
                db.Submissions.Add(new Submission { ProblemId = problemId, UserId = kidId, Code = "x", Status = SubmissionStatus.Queued });   // not finished: does not count
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9" })).StatusCode);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var kidId = (await db.Users.FirstAsync(u => u.Email == "kid@test.dev")).Id;
                db.Submissions.Add(new Submission { ProblemId = (await db.Problems.FirstAsync()).Id, UserId = kidId, Code = "x", Status = SubmissionStatus.Done });
                await db.SaveChangesAsync();
            }
            Assert.True((await kid.GetFromJsonAsync<JsonElement>("/api/survey/status")).GetProperty("eligible").GetBoolean());

            (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9", ["like"] = "  fast  ", ["empty"] = "  " })).EnsureSuccessStatusCode();
            (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "10" })).EnsureSuccessStatusCode();
            var mine = await kid.GetFromJsonAsync<Dictionary<string, string>>("/api/survey/mine");
            Assert.Equal(new Dictionary<string, string> { ["nps"] = "10" }, mine);
            using (var scope = app.Services.CreateScope())
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().SurveyResponses.CountAsync());

            Assert.Equal(HttpStatusCode.BadRequest, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["Bad Key!"] = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["like"] = new string('x', 501) })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.GetAsync("/api/survey/results")).StatusCode);

            var boss = app.CreateClient();
            (await boss.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var res = await boss.GetAsync("/api/survey/results");
            res.EnsureSuccessStatusCode();
            var text = await res.Content.ReadAsStringAsync();
            Assert.Contains("\"nps\":\"10\"", text);
            Assert.DoesNotContain("kid@test.dev", text);
            Assert.DoesNotContain("userId", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    
    }

    [Fact]
    public async Task A_closed_survey_takes_no_answers_and_says_so_in_its_status()
    {
        Environment.SetEnvironmentVariable("Survey__Open", "false");
        try
        {
            using var app = new TestApp();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "late@test.dev", password = "Passw0rd!x1", displayName = "Late", role = "Student" })).EnsureSuccessStatusCode();
            Assert.False((await kid.GetFromJsonAsync<JsonElement>("/api/survey/status")).GetProperty("open").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9" })).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("Survey__Open", null); }
    }
}
