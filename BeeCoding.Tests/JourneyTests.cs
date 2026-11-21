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
public class JourneyTests
{
    [Fact]
    public async Task Ordered_path_with_skips_and_a_boss_that_pays_once()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
            var admin = app.CreateClient();
            (await admin.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

            int kidId; List<string> slugs; List<int> ids;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
                kidId = (await db.Users.FirstAsync(u => u.Email == "kid@test.dev")).Id;
                db.BankProblems.AddRange(Enumerable.Range(1, 4).Select(i => new BankProblem
                {
                    OwnerId = owner.Id, Title = $"P{i}", StatementMarkdown = "s", IsPublic = i != 4, Level = ProblemLevel.Easy, Tags = "loop",
                    TestCases = [new() { Stdin = "1", ExpectedStdout = "1", IsSample = true }],
                }));
                await db.SaveChangesAsync();
                var rows = await db.BankProblems.Where(b => b.Title.StartsWith("P")).OrderBy(b => b.Title).ToListAsync();
                slugs = rows.Select(r => r.Slug).ToList();
                ids = rows.Select(r => r.Id).ToList();
            }
            // P1, P2 in the world, P3 is the boss, P4 is private

            Assert.Equal(HttpStatusCode.Forbidden, (await kid.PostAsJsonAsync("/api/journeys/admin", new { title = "x", published = true, worlds = new object[0] })).StatusCode);
            var bad = await admin.PostAsJsonAsync("/api/journeys/admin", new { title = "J", published = true, worlds = new[] { new { name = "W", unlockAfter = 1, bossSlug = slugs[2], bossXp = 100, badgeName = "B", slugs = new[] { slugs[3] } } } });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);   // private problem

            var make = await admin.PostAsJsonAsync("/api/journeys/admin", new { title = "J", published = true, worlds = new[] { new { name = "W1", unlockAfter = 1, bossSlug = slugs[2], bossXp = 100, badgeName = "Slayer", slugs = new[] { slugs[0], slugs[1] } } } });
            make.EnsureSuccessStatusCode();
            var jid = (await make.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            async Task<JsonElement> Map() => await kid.GetFromJsonAsync<JsonElement>($"/api/journeys/{jid}");
            string[] States(JsonElement m) => m.GetProperty("worlds")[0].GetProperty("items").EnumerateArray().Select(i => i.GetProperty("state").GetString()!).ToArray();
            string Boss(JsonElement m) => m.GetProperty("worlds")[0].GetProperty("boss").GetProperty("state").GetString()!;
            async Task Solve(int bankId)
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.BankSubmissions.Add(new BankSubmission { BankProblemId = bankId, UserId = kidId, Code = "//", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, Score = 1.0 });
                await db.SaveChangesAsync();
            }
            async Task<int> Xp() { using var s = app.Services.CreateScope(); return (await s.ServiceProvider.GetRequiredService<AppDbContext>().Users.FirstAsync(u => u.Id == kidId)).Xp; }

            var m = await Map();
            Assert.Equal(new[] { "next", "todo" }, States(m));
            Assert.Equal("locked", Boss(m));

            Assert.Equal(HttpStatusCode.NoContent, (await kid.PostAsync($"/api/journeys/{jid}/skip/{slugs[0]}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await kid.PostAsync($"/api/journeys/{jid}/skip/{slugs[0]}", null)).StatusCode);   // idempotent
            Assert.Equal(HttpStatusCode.NotFound, (await kid.PostAsync($"/api/journeys/{jid}/skip/{slugs[2]}", null)).StatusCode);      // the boss is not skippable
            m = await Map();
            Assert.Equal(new[] { "skipped", "next" }, States(m));      // order stays, the skipped one is passed over
            Assert.Equal("locked", Boss(m));                           // skipped does not count

            await Solve(ids[1]);
            m = await Map();
            Assert.Equal(new[] { "skipped", "done" }, States(m));
            Assert.Equal("ready", Boss(m));                            // 1 of 1 needed

            await Solve(ids[2]);
            var xp0 = await Xp();
            m = await Map();
            Assert.Equal("defeated", Boss(m));
            Assert.Equal(1, m.GetProperty("newlyDefeated").GetArrayLength());
            Assert.Equal(xp0 + 100, await Xp());
            m = await Map();                                           // reopening pays nothing more
            Assert.Equal(0, m.GetProperty("newlyDefeated").GetArrayLength());
            Assert.Equal(xp0 + 100, await Xp());

            // an edit replaces the worlds' ids: the boss still must not pay twice
            (await admin.PutAsJsonAsync($"/api/journeys/admin/{jid}", new { title = "J2", published = true, worlds = new[] { new { name = "W1b", unlockAfter = 1, bossSlug = slugs[2], bossXp = 100, badgeName = "Slayer", slugs = new[] { slugs[0], slugs[1] } } } })).EnsureSuccessStatusCode();
            m = await Map();
            Assert.Equal(0, m.GetProperty("newlyDefeated").GetArrayLength());
            Assert.Equal(xp0 + 100, await Xp());

            (await admin.PutAsJsonAsync($"/api/journeys/admin/{jid}", new { title = "J2", published = false, worlds = new object[0] })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync($"/api/journeys/{jid}")).StatusCode);   // unpublished is invisible
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
