using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AutomationBlockTests
{
    [Fact]
    public async Task A_student_browser_that_says_it_is_automated_is_refused_the_protected_statement_and_it_is_audited()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        string problemId, bankId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.Slug == "demo-board");
            db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = (await db.Users.FirstAsync(u => u.Email == "kid@test.dev")).Id, Role = MembershipRole.Student });
            var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var bank = new BankProblem { OwnerId = owner.Id, Title = "Pub", StatementMarkdown = "s", IsPublic = true };
            db.BankProblems.Add(bank);
            await db.SaveChangesAsync();
            problemId = (await db.Problems.FirstAsync(p => p.BoardId == board.Id)).Slug;
            bankId = bank.Slug;
        }

        HttpRequestMessage Get(string url, string? flags) { var r = new HttpRequestMessage(HttpMethod.Get, url); if (flags is not null) r.Headers.Add("X-Client-Flags", flags); return r; }

        foreach (var url in new[] { $"/api/problems/{problemId}/statement", $"/api/practice/{bankId}/statement" })
        {
            Assert.Equal(HttpStatusCode.OK, (await kid.SendAsync(Get(url, null))).StatusCode);                 // a normal browser is served
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.SendAsync(Get(url, "webdriver,headless"))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await teacher.SendAsync(Get($"/api/problems/{problemId}/statement", "webdriver"))).StatusCode);   // staff may use automation (tests, demos)

        using var check = app.Services.CreateScope();
        Assert.Equal(2, await check.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.CountAsync(a => a.Action == "automation-blocked"));
    }
}
