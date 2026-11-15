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
        Assert.Equal(2, (await svc.ComputeForBoardAsync(boardId)).Count);                       // both problems
        var only = await svc.ComputeForBoardAsync(boardId, a);
        Assert.Single(only);
        Assert.Equal(a, only[0].ProblemId);

        var t = app.CreateClient();
        (await t.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        Assert.Single(await t.GetFromJsonAsync<List<PlagiarismPairDto>>($"/api/boards/{boardSlug}/plagiarism?problem={slugA}") ?? []);
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
                Assert.Equal(2, (await t.GetFromJsonAsync<List<PlagiarismPairDto>>($"{root}/plagiarism?boardId={boardId}"))!.Count);
                Assert.Single((await t.GetFromJsonAsync<List<PlagiarismPairDto>>($"{root}/plagiarism?boardId={boardId}&problemId={a}"))!);
                Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"{root}/plagiarism?boardId={boardId}&problemId={otherBoardProblem}")).StatusCode);   // not this board's problem
            }
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
