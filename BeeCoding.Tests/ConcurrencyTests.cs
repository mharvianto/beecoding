using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class ConcurrencyTests
{
    [Fact]
    public async Task Two_simultaneous_approvals_copy_the_problem_once()
    {
        using var app = new TestApp();
        int proposalId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.Slug == "demo-board");
            var student = new User { Email = "s@test.dev", DisplayName = "S", Role = UserRole.Student, PasswordHash = "x" };
            db.Users.Add(student);
            await db.SaveChangesAsync();
            var bank = new BankProblem { OwnerId = student.Id, Title = "Proposed once", StatementMarkdown = "s" };
            bank.TestCases.Add(new BankTestCase { Stdin = "1\n", ExpectedStdout = "1\n", Points = 1 });
            var p = new ProblemProposal { BoardId = board.Id, ProposerId = student.Id, BankProblem = bank };
            db.ProblemProposals.Add(p);
            await db.SaveChangesAsync();
            proposalId = p.Id;
        }

        var teacher = app.CreateClient();
        var login = await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
        login.EnsureSuccessStatusCode();

        var url = $"/api/boards/demo-board/proposals/{proposalId}/approve";
        var codes = (await Task.WhenAll(teacher.PostAsync(url, null), teacher.PostAsync(url, null))).Select(r => r.StatusCode).OrderBy(c => c).ToArray();

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, codes);
        using var check = app.Services.CreateScope();
        var copies = await check.ServiceProvider.GetRequiredService<AppDbContext>().Problems.CountAsync(x => x.Title == "Proposed once");
        Assert.Equal(1, copies);
    }

    [Fact]
    public async Task A_submission_left_queued_by_a_restart_is_graded_on_the_next_start()
    {
        var db = Path.Combine(Path.GetTempPath(), $"beecoding-test-{Guid.NewGuid():N}.db");
        int id;
        using (var first = new TestApp(db))
        using (var scope = first.Services.CreateScope())   // first start: migrate + seed; nothing is queued yet
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = new Submission
            {
                ProblemId = (await ctx.Problems.FirstAsync()).Id, UserId = (await ctx.Users.FirstAsync()).Id, Language = "c",
                Code = "#include <stdio.h>\nint main(){int a,b;scanf(\"%d %d\",&a,&b);printf(\"%d\\n\",a+b);}",
            };
            ctx.Submissions.Add(s);
            await ctx.SaveChangesAsync();
            id = s.Id;
        }   // the in-memory queue dies here, as it would on a deploy

        using var second = new TestApp(db);
        try
        {
        var status = SubmissionStatus.Queued;
        for (int i = 0; i < 60 && status != SubmissionStatus.Done; i++)
        {
            await Task.Delay(500);
            using var scope = second.Services.CreateScope();
            status = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Submissions.Where(x => x.Id == id).Select(x => x.Status).FirstAsync();
        }
        Assert.Equal(SubmissionStatus.Done, status);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var f in new[] { db, db + "-wal", db + "-shm" }) try { File.Delete(f); } catch { } }
    }
}
