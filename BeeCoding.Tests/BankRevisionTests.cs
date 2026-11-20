using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class BankRevisionTests
{
    [Fact]
    public async Task A_student_revises_a_live_problem_through_a_private_copy_and_approval_writes_it_into_the_live_one()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var admin = app.CreateClient();
            (await admin.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
            var other = app.CreateClient();
            (await other.PostAsJsonAsync("/api/auth/register", new { email = "other@test.dev", password = "Passw0rd!x1", displayName = "Ani", role = "Student" })).EnsureSuccessStatusCode();

            string slug;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Users.FirstAsync(u => u.Email == "kid@test.dev");
                var b = new BankProblem
                {
                    OwnerId = owner.Id, Title = "Old title", StatementMarkdown = "old", IsPublic = true, Level = ProblemLevel.Easy,
                    TestCases = [new() { Stdin = "1", ExpectedStdout = "WRONG", IsSample = true, Position = 0 }, new() { Stdin = "2", ExpectedStdout = "WRONG", Position = 1 }],
                };
                db.BankProblems.Add(b);
                await db.SaveChangesAsync();
                slug = b.Slug;
            }

            static object Body(string title, string expected) => new
            {
                title, statementMarkdown = "new", allowedLanguages = "", timeLimitMs = 1000, memoryLimitKb = 32768, level = "Easy", tags = "", isPublic = false,
                testCases = new[] { new { id = (int?)null, stdin = "1", expectedStdout = expected, isSample = true, points = 1, position = 0 }, new { id = (int?)null, stdin = "2", expectedStdout = expected, isSample = false, points = 1, position = 1 } },
            };

            // a live problem cannot be edited, only revised, and only by its owner
            Assert.Equal(HttpStatusCode.Conflict, (await kid.PutAsJsonAsync($"/api/bank/{slug}", Body("X", "ok"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/bank/{slug}/revise", null)).StatusCode);

            var made = await kid.PostAsync($"/api/bank/{slug}/revise", null);
            made.EnsureSuccessStatusCode();
            var draft = await made.Content.ReadFromJsonAsync<BankProblemDto>();
            Assert.NotEqual(slug, draft!.Slug);
            Assert.False(draft.IsPublic);
            Assert.Equal(2, draft.TestCases.Count);
            Assert.Equal(HttpStatusCode.Conflict, (await kid.PostAsync($"/api/bank/{slug}/revise", null)).StatusCode);   // one revision at a time

            // the copy is editable and goes through review; the live problem is untouched meanwhile
            (await kid.PutAsJsonAsync($"/api/bank/{draft.Slug}", Body("New title", "ok"))).EnsureSuccessStatusCode();
            (await kid.PostAsync($"/api/bank/{draft.Slug}/submit-practice", null)).EnsureSuccessStatusCode();
            var live = await kid.GetFromJsonAsync<BankProblemDto>($"/api/bank/{slug}");
            Assert.Equal("Old title", live!.Title);

            var queue = await admin.GetFromJsonAsync<List<AdminAiReviewRow>>("/api/admin-ui/ai-review");
            var row = Assert.Single(queue!);
            Assert.Equal(slug, row.RevisionOfSlug);

            (await admin.PostAsync($"/api/admin-ui/ai-review/{draft.Slug}/approve", null)).EnsureSuccessStatusCode();

            live = await kid.GetFromJsonAsync<BankProblemDto>($"/api/bank/{slug}");
            Assert.Equal("New title", live!.Title);
            Assert.True(live.IsPublic);
            Assert.Equal(2, live.TestCases.Count);
            Assert.All(live.TestCases, t => Assert.Equal("ok", t.ExpectedStdout));
            Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync($"/api/bank/{draft.Slug}")).StatusCode);   // the draft is retired
            Assert.Empty((await admin.GetFromJsonAsync<List<AdminAiReviewRow>>("/api/admin-ui/ai-review"))!);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
