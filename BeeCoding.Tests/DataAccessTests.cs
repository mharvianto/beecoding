using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class DataAccessTests
{
    [Fact]
    public async Task A_draft_bank_problem_is_open_to_its_owner_only_and_never_listed_in_practice()
    {
        using var app = new TestApp();
        string slug;
        int id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var p = new BankProblem { OwnerId = owner.Id, Title = "Draft only", StatementMarkdown = "x", IsPublic = false };
            p.TestCases.Add(new BankTestCase { Stdin = "1", ExpectedStdout = "1", IsSample = true });
            db.BankProblems.Add(p);
            await db.SaveChangesAsync();
            slug = p.Slug; id = p.Id;
        }

        var ownerHttp = app.CreateClient();
        (await ownerHttp.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await ownerHttp.GetAsync($"/api/practice/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerHttp.GetAsync($"/api/practice/{id}/statement")).StatusCode);
        Assert.DoesNotContain(slug, await ownerHttp.GetStringAsync("/api/practice"));

        var other = app.CreateClient();
        (await other.PostAsJsonAsync("/api/auth/register", new { email = "other@test.dev", password = "Passw0rd!x1", displayName = "Other", role = "Student" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/practice/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/practice/{id}/statement")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/practice/{slug}/submit", new { code = "int main(){}", language = "c" })).StatusCode);
    }

    [Fact]
    public async Task Topic_stats_count_attempts_accepted_and_distinct_solvers_per_tag()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
                var s1 = new User { Email = "a@test.dev", DisplayName = "A", Role = UserRole.Student, PasswordHash = "x" };
                var s2 = new User { Email = "b@test.dev", DisplayName = "B", Role = UserRole.Student, PasswordHash = "x" };
                db.Users.AddRange(s1, s2);
                var p = new BankProblem { OwnerId = owner.Id, Title = "T", StatementMarkdown = "x", Tags = "zz-topic", IsPublic = true };
                db.BankProblems.Add(p);
                await db.SaveChangesAsync();
                BankSubmission S(User u, Verdict v, double score) => new() { BankProblemId = p.Id, UserId = u.Id, Code = "x", Verdict = v, Score = score };
                db.BankSubmissions.AddRange(S(s1, Verdict.Accepted, 1), S(s1, Verdict.Accepted, 1), S(s2, Verdict.WrongAnswer, 0.5), S(s2, Verdict.Accepted, 1));
                await db.SaveChangesAsync();
            }

            var http = app.CreateClient();
            (await http.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var rows = await http.GetFromJsonAsync<JsonElement>("/api/admin-ui/dashboard/topics?take=50");
            var row = rows.EnumerateArray().Single(r => r.GetProperty("tag").GetString() == "zz-topic");
            Assert.Equal(4, row.GetProperty("attempts").GetInt32());
            Assert.Equal(3, row.GetProperty("accepted").GetInt32());
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task The_engagement_chart_has_one_point_per_period_and_ignores_submissions_outside_the_window()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
                var p = new BankProblem { OwnerId = owner.Id, Title = "E", StatementMarkdown = "x", IsPublic = true };
                db.BankProblems.Add(p);
                await db.SaveChangesAsync();
                BankSubmission At(int daysAgo) => new() { BankProblemId = p.Id, UserId = owner.Id, Code = "x", CreatedAt = DateTime.UtcNow.AddDays(-daysAgo) };
                db.BankSubmissions.AddRange(At(0), At(3), At(3), At(40));   // 40 days ago is outside a 14-day window
                await db.SaveChangesAsync();
            }

            var http = app.CreateClient();
            (await http.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var points = (await http.GetFromJsonAsync<JsonElement>("/api/admin-ui/dashboard/engagement?granularity=day&periods=14")).EnumerateArray().ToList();
            Assert.Equal(14, points.Count);
            Assert.Equal(3, points.Sum(x => x.GetProperty("submissions").GetInt32()));
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task The_audit_sweep_drops_rows_older_than_a_year_and_keeps_newer_ones()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogEntries.Add(new AuditLogEntry { ActorEmail = "a", Action = "delete", TargetType = "Board", TargetLabel = "old", CreatedAt = DateTime.UtcNow.AddDays(-400) });
        db.AuditLogEntries.Add(new AuditLogEntry { ActorEmail = "a", Action = "delete", TargetType = "Board", TargetLabel = "new", CreatedAt = DateTime.UtcNow.AddDays(-30) });
        await db.SaveChangesAsync();
        var before = await db.AuditLogEntries.CountAsync();

        var janitor = new AuditLogJanitor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), NullLogger<AuditLogJanitor>.Instance);
        Assert.Equal(1, await janitor.SweepAsync(CancellationToken.None));
        Assert.Equal(before - 1, await db.AuditLogEntries.CountAsync());
        Assert.False(await db.AuditLogEntries.AnyAsync(x => x.TargetLabel == "old"));
    }
}
