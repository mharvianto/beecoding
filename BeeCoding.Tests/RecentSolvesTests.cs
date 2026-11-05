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
public class RecentSolvesTests
{
    [Fact]
    public async Task Recent_solves_list_each_first_practice_solve_of_public_problems_only_newest_first()
    {
        using var app = new TestApp();
        HttpClient alice = app.CreateClient(), bob = app.CreateClient();
        (await alice.PostAsJsonAsync("/api/auth/register", new { email = "alice@test.dev", password = "Passw0rd!x1", displayName = "Alice Wati", role = "Student" })).EnsureSuccessStatusCode();
        (await bob.PostAsJsonAsync("/api/auth/register", new { email = "bob@test.dev", password = "Passw0rd!x1", displayName = "Bob Santoso", role = "Student" })).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = await db.Users.FirstAsync(u => u.Email == "alice@test.dev");
            var b = await db.Users.FirstAsync(u => u.Email == "bob@test.dev");
            var teacher = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            BankProblem Make(int owner, string title, bool pub) => new() { OwnerId = owner, Title = title, StatementMarkdown = "x", IsPublic = pub };
            var open = Make(teacher.Id, "Soal Terbuka", true);
            var secret = Make(teacher.Id, "Soal Rahasia", false);       // private: its title must not leak
            var hers = Make(a.Id, "Soal Buatan Alice", true);           // solved by its own author
            db.BankProblems.AddRange(open, secret, hers);
            await db.SaveChangesAsync();
            var t0 = DateTime.UtcNow.AddMinutes(-30);
            BankSubmission Sub(int user, int problem, Verdict v, double score, int minutes) =>
                new() { UserId = user, BankProblemId = problem, Code = "x", Status = SubmissionStatus.Done, Verdict = v, Score = score, CreatedAt = t0.AddMinutes(minutes) };
            db.BankSubmissions.AddRange(
                Sub(a.Id, open.Id, Verdict.WrongAnswer, 0, 0),
                Sub(a.Id, open.Id, Verdict.Accepted, 1.0, 5),            // Alice's first solve
                Sub(a.Id, open.Id, Verdict.Accepted, 1.0, 20),           // solving again must not add a second row
                Sub(a.Id, secret.Id, Verdict.Accepted, 1.0, 6),
                Sub(a.Id, hers.Id, Verdict.Accepted, 1.0, 7),
                Sub(b.Id, open.Id, Verdict.Accepted, 0.5, 8),            // partial score is not a solve
                Sub(b.Id, open.Id, Verdict.Accepted, 1.0, 10));          // Bob solved it later than Alice
            await db.SaveChangesAsync();
        }

        var rows = JsonDocument.Parse(await bob.GetStringAsync("/api/leaderboard/recent")).RootElement.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Bob Santoso", rows[0].GetProperty("displayName").GetString());      // newest first, and the full name
        Assert.True(rows[0].GetProperty("me").GetBoolean());
        Assert.Equal("Alice Wati", rows[1].GetProperty("displayName").GetString());
        Assert.False(rows[1].GetProperty("me").GetBoolean());
        Assert.All(rows, r => Assert.Equal("Soal Terbuka", r.GetProperty("problemTitle").GetString()));   // no private title, no self-authored one
        Assert.NotEmpty(rows[0].GetProperty("problemSlug").GetString()!);

        // a scope the caller does not belong to is refused, like the ranking
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/leaderboard/recent?organizationId=9999")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/leaderboard/recent?boardId=9999")).StatusCode);
    }
}
