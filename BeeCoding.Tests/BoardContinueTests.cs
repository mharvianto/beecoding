using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class BoardContinueTests
{
    [Fact]
    public async Task Practice_picks_appear_only_for_a_board_student_who_solved_every_visible_problem()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
        var stranger = app.CreateClient();
        (await stranger.PostAsJsonAsync("/api/auth/register", new { email = "x@test.dev", password = "Passw0rd!x1", displayName = "Xan", role = "Student" })).EnsureSuccessStatusCode();
        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

        int studentId; List<int> visible;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.Include(b => b.Problems).FirstAsync(b => b.Slug == "demo-board");
            studentId = (await db.Users.FirstAsync(u => u.Email == "kid@test.dev")).Id;
            db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = studentId, Role = MembershipRole.Student });
            var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            db.BankProblems.AddRange(Enumerable.Range(1, 3).Select(i => new BankProblem
            {
                OwnerId = owner.Id, Title = $"Pub {i}", StatementMarkdown = "s", IsPublic = true, Level = ProblemLevel.Medium, Tags = "loop",
                TestCases = [new() { Stdin = "1", ExpectedStdout = "1", IsSample = true }, new() { Stdin = "2", ExpectedStdout = "2" }],
            }));
            db.Problems.Add(new Problem { BoardId = board.Id, Title = "Secret", StatementMarkdown = "s", Hidden = true, Position = 99 });   // unseen by the student: must not hold the picks back
            await db.SaveChangesAsync();
            visible = board.Problems.Where(p => !p.Hidden).Select(p => p.Id).ToList();
        }
        Assert.NotEmpty(visible);

        async Task<List<RecommendationDto>> Picks(HttpClient c) => (await c.GetFromJsonAsync<List<RecommendationDto>>("/api/practice/after-board/demo-board"))!;
        async Task Solve(int problemId, double score, Verdict v)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Submissions.Add(new Submission { ProblemId = problemId, UserId = studentId, Language = "cpp", Code = "//", Status = SubmissionStatus.Done, Verdict = v, Score = score });
            await db.SaveChangesAsync();
        }

        Assert.Empty(await Picks(kid));                                    // nothing solved yet
        foreach (var id in visible.Skip(1)) await Solve(id, 1.0, Verdict.Accepted);
        await Solve(visible[0], 0.5, Verdict.WrongAnswer);                 // a partial score does not count as solved
        Assert.Empty(await Picks(kid));
        await Solve(visible[0], 1.0, Verdict.Accepted);

        var picks = await Picks(kid);
        Assert.NotEmpty(picks);
        Assert.True(picks.Count <= 3);
        Assert.Empty(await Picks(stranger));                               // not a member of the board
        Assert.Empty(await Picks(teacher));                                // staff are not students
    }
}
