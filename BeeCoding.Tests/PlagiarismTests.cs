using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class PlagiarismTests
{
    const string Code = "#include <stdio.h>\nint main(){int a,b;scanf(\"%d %d\",&a,&b);int s=a+b;if(s>10){printf(\"%d\\n\",s*2);}else{printf(\"%d\\n\",s);}return 0;}\n";

    [Fact]
    public async Task One_problem_can_be_checked_alone_and_an_unknown_one_is_a_404()
    {
        using var app = new TestApp();
        (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Kid", role = "Student" })).EnsureSuccessStatusCode();
        string boardSlug, slugA; int a;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
            boardSlug = board.Slug;
            var pa = new Problem { BoardId = board.Id, Title = "A", StatementMarkdown = "x" };
            var pb = new Problem { BoardId = board.Id, Title = "B", StatementMarkdown = "x" };
            db.Problems.AddRange(pa, pb);
            await db.SaveChangesAsync();
            var users = await db.Users.Where(u => u.Email == "teacher@demo.test" || u.Email == "kid@test.dev").ToListAsync();
            foreach (var p in new[] { pa, pb })
                foreach (var u in users)
                    db.Submissions.Add(new Submission { ProblemId = p.Id, UserId = u.Id, Code = Code, Status = SubmissionStatus.Done });
            await db.SaveChangesAsync();
            slugA = pa.Slug; a = pa.Id;
        }

        using var scope2 = app.Services.CreateScope();
        var svc = scope2.ServiceProvider.GetRequiredService<PlagiarismService>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardId = (await db2.Boards.FirstAsync(b => b.Slug == boardSlug)).Id;
        Assert.Equal(2, (await svc.ComputeForBoardAsync(boardId)).Pairs.Count);                 // both problems
        var only = await svc.ComputeForBoardAsync(boardId, a);
        Assert.Single(only.Pairs);
        Assert.Equal(a, only.Pairs[0].ProblemId);

        var t = app.CreateClient();
        (await t.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        Assert.Single((await t.GetFromJsonAsync<PlagiarismResultDto>($"/api/boards/{boardSlug}/plagiarism?problem={slugA}"))!.Pairs);
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/boards/{boardSlug}/plagiarism?problem=nope")).StatusCode);
    }

    [Fact]
    public async Task Platform_admin_and_org_admin_can_list_a_boards_problems_and_compare_one()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");   // a platform admin may manage every organization
        try
        {
            using var app = new TestApp();
            (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Kid", role = "Student" })).EnsureSuccessStatusCode();
            int orgId, boardId, a, otherBoardProblem;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
                var org = new Organization { Name = "O", Slug = "o-plag" };
                db.Organizations.Add(org);
                await db.SaveChangesAsync();
                var board = new Board { Title = "In org", OwnerId = owner.Id, OrganizationId = org.Id, JoinCode = "ORGPLG" };
                db.Boards.Add(board);
                await db.SaveChangesAsync();
                var pa = new Problem { BoardId = board.Id, Title = "A", StatementMarkdown = "x", Position = 0 };
                var pb = new Problem { BoardId = board.Id, Title = "B", StatementMarkdown = "x", Position = 1 };
                var foreign = new Problem { BoardId = (await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01")).Id, Title = "Elsewhere", StatementMarkdown = "x" };
                db.Problems.AddRange(pa, pb, foreign);
                await db.SaveChangesAsync();
                foreach (var p in new[] { pa, pb })
                    foreach (var u in await db.Users.Where(u => u.Email == "teacher@demo.test" || u.Email == "kid@test.dev").ToListAsync())
                        db.Submissions.Add(new Submission { ProblemId = p.Id, UserId = u.Id, Code = Code, Status = SubmissionStatus.Done });
                await db.SaveChangesAsync();
                orgId = org.Id; boardId = board.Id; a = pa.Id; otherBoardProblem = foreign.Id;
            }
            var t = app.CreateClient();
            (await t.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

            foreach (var root in new[] { "/api/admin-ui", $"/api/org-admin/{orgId}" })
            {
                var problems = await t.GetFromJsonAsync<List<PlagiarismProblemDto>>($"{root}/plagiarism/problems?boardId={boardId}") ?? [];
                Assert.Equal(["A", "B"], problems.Select(p => p.Title));
                Assert.Equal(2, (await t.GetFromJsonAsync<PlagiarismResultDto>($"{root}/plagiarism?boardId={boardId}"))!.Pairs.Count);
                Assert.Single((await t.GetFromJsonAsync<PlagiarismResultDto>($"{root}/plagiarism?boardId={boardId}&problemId={a}"))!.Pairs);
                Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"{root}/plagiarism?boardId={boardId}&problemId={otherBoardProblem}")).StatusCode);   // not this board's problem
            }
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task Three_or_more_alike_students_become_one_group_and_two_stay_a_pair()
    {
        using var app = new TestApp();
        foreach (var n in new[] { "s1", "s2", "s3" })
            (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = $"{n}@test.dev", password = "Passw0rd!x1", displayName = n, role = "Student" })).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
        var many = new Problem { BoardId = board.Id, Title = "Many", StatementMarkdown = "x" };
        var two = new Problem { BoardId = board.Id, Title = "Two", StatementMarkdown = "x" };
        db.Problems.AddRange(many, two);
        await db.SaveChangesAsync();
        var users = await db.Users.Where(u => u.Email == "teacher@demo.test" || u.Email.EndsWith("@test.dev")).OrderBy(u => u.Id).ToListAsync();   // 4 people
        foreach (var u in users) db.Submissions.Add(new Submission { ProblemId = many.Id, UserId = u.Id, Code = Code, Status = SubmissionStatus.Done });
        foreach (var u in users.Take(2)) db.Submissions.Add(new Submission { ProblemId = two.Id, UserId = u.Id, Code = Code, Status = SubmissionStatus.Done });
        await db.SaveChangesAsync();

        var r = await scope.ServiceProvider.GetRequiredService<PlagiarismService>().ComputeForBoardAsync(board.Id);

        var g = Assert.Single(r.Groups);
        Assert.Equal("Many", g.ProblemTitle);
        Assert.Equal(4, g.Members.Count);
        Assert.Equal(1.0, g.MinSimilarity);
        Assert.Equal(6, g.StrongestPairs.Count);                          // 4 students = 6 pairs, shown inside the group
        var pair = Assert.Single(r.Pairs);                                // not 6 + 1 loose rows
        Assert.Equal("Two", pair.ProblemTitle);
    }

    [Fact]
    public async Task A_chain_A_like_B_and_B_like_C_is_one_group_whose_lowest_score_is_the_weak_link()
    {
        // 40 numbered lines; B changes 5 of A's, C changes 5 more of B's: A~B and B~C pass 0.65, A~C does not.
        static string Prog(int from, int to) =>
            "int main(){" + string.Concat(Enumerable.Range(1, 40).Select(k => $"a={(k >= from && k <= to ? k + 100 : k)};")) + "return 0;}";
        using var app = new TestApp();
        foreach (var n in new[] { "s1", "s2" })
            (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = $"{n}@test.dev", password = "Passw0rd!x1", displayName = n, role = "Student" })).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
        var p = new Problem { BoardId = board.Id, Title = "Chain", StatementMarkdown = "x" };
        db.Problems.Add(p);
        await db.SaveChangesAsync();
        var users = await db.Users.Where(u => u.Email == "teacher@demo.test" || u.Email.EndsWith("@test.dev")).OrderBy(u => u.Id).Take(3).ToListAsync();
        var codes = new[] { Prog(0, 0), Prog(1, 5), Prog(1, 10) };
        for (var i = 0; i < 3; i++) db.Submissions.Add(new Submission { ProblemId = p.Id, UserId = users[i].Id, Code = codes[i], Status = SubmissionStatus.Done });
        await db.SaveChangesAsync();

        var r = await scope.ServiceProvider.GetRequiredService<PlagiarismService>().ComputeForBoardAsync(board.Id);

        var g = Assert.Single(r.Groups);
        Assert.Equal(3, g.Members.Count);
        Assert.True(g.MinSimilarity < PlagiarismService.GroupThreshold, $"min {g.MinSimilarity}");
        Assert.True(g.MaxSimilarity >= PlagiarismService.GroupThreshold, $"max {g.MaxSimilarity}");
        Assert.Empty(r.Pairs);
    }
}
