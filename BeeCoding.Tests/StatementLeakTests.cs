using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class StatementLeakTests
{
    [Fact]
    public async Task A_board_student_never_gets_the_statement_or_expected_output_as_JSON_but_staff_do()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        string slug;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.Slug == "demo-board");
            var kidId = (await db.Users.FirstAsync(u => u.Email == "kid@test.dev")).Id;
            db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = kidId, Role = MembershipRole.Student });
            var p = new Problem { BoardId = board.Id, Title = "Secret statement", StatementMarkdown = "THE-SECRET-TEXT", Position = 99,
                TestCases = [new() { Stdin = "SAMPLE-IN", ExpectedStdout = "SAMPLE-OUT", IsSample = true }] };
            db.Problems.Add(p);
            await db.SaveChangesAsync();
            slug = p.Slug;
        }

        var forKid = await kid.GetStringAsync($"/api/boards/demo-board/problems/{slug}");
        Assert.DoesNotContain("THE-SECRET-TEXT", forKid);
        Assert.DoesNotContain("SAMPLE-OUT", forKid);
        Assert.Contains("SAMPLE-IN", forKid);                                           // the Run box is still filled
        var list = await kid.GetStringAsync("/api/boards/demo-board/problems");
        Assert.DoesNotContain("THE-SECRET-TEXT", list);
        Assert.DoesNotContain("SAMPLE-OUT", list);

        var forTeacher = await teacher.GetStringAsync($"/api/boards/demo-board/problems/{slug}");
        Assert.Contains("THE-SECRET-TEXT", forTeacher);                                 // staff edit it, so they get the text
        Assert.Contains("SAMPLE-OUT", forTeacher);
    }
}
