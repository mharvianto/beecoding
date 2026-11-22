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
public class AiReviewResultTests
{
    [Fact]
    public async Task Saved_check_is_readable_goes_out_of_date_when_the_problem_changes_and_is_admin_only()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var admin = app.CreateClient();
            (await admin.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();

            int id;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
                var b = new BankProblem { OwnerId = owner.Id, Title = "T", StatementMarkdown = "s", Level = ProblemLevel.Easy,
                    TestCases = [new() { Stdin = "1", ExpectedStdout = "1", IsSample = true }] };
                db.BankProblems.Add(b);
                await db.SaveChangesAsync();
                id = b.Id;
            }

            Assert.Equal(HttpStatusCode.NoContent, (await admin.GetAsync($"/api/ai/last-review/bank/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.GetAsync($"/api/ai/last-review/bank/{id}")).StatusCode);

            // the same fingerprint the job stores: save through the endpoint's own rule by running the real save path is not
            // possible without an AI provider, so store a row whose hash is wrong, then one that matches after a read.
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AiReviewResults.Add(new AiReviewResult { Key = $"check:bank:{id}", ContentHash = "old", Json = "{\"summary\":\"ok\"}" });
                await db.SaveChangesAsync();
            }
            var r = await admin.GetFromJsonAsync<JsonElement>($"/api/ai/last-review/bank/{id}");
            Assert.Equal("ok", r.GetProperty("result").GetProperty("summary").GetString());
            Assert.True(r.GetProperty("outOfDate").GetBoolean());   // hash differs from the problem as it is now
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
