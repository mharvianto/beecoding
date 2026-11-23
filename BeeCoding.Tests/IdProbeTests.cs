using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class IdProbeTests
{
    [Fact]
    public async Task Someone_outside_a_board_gets_the_same_404_for_an_id_that_exists_and_one_that_does_not()
    {
        using var app = new TestApp();
        int problemId, submissionId, postId, practiceSubId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var teacher = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var board = await db.Boards.Include(b => b.Problems).FirstAsync(b => b.JoinCode == "DEMO01");
            var p = board.Problems.First();
            var sub = new Submission { ProblemId = p.Id, UserId = teacher.Id, Code = "//", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, Score = 1 };
            var post = new Post { BoardId = board.Id, ProblemId = p.Id, UserId = teacher.Id };
            var bank = new BankProblem { OwnerId = teacher.Id, Title = "PB", StatementMarkdown = "s", IsPublic = true, Level = ProblemLevel.Easy };
            db.BankProblems.Add(bank);
            await db.SaveChangesAsync();
            var bsub = new BankSubmission { BankProblemId = bank.Id, UserId = teacher.Id, Code = "//", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, Score = 1 };
            db.Submissions.Add(sub); db.Posts.Add(post); db.BankSubmissions.Add(bsub);
            await db.SaveChangesAsync();
            (problemId, submissionId, postId, practiceSubId) = (p.Id, sub.Id, post.Id, bsub.Id);
        }
        var outsider = app.CreateClient();
        (await outsider.PostAsJsonAsync("/api/auth/register", new { email = "out@test.dev", password = "Passw0rd!x1", displayName = "Out", role = "Student" })).EnsureSuccessStatusCode();

        async Task<HttpStatusCode> Code(Func<HttpClient, Task<HttpResponseMessage>> call) => (await call(outsider)).StatusCode;
        var cases = new (string Name, Func<int, Func<HttpClient, Task<HttpResponseMessage>>> Call, int Real)[]
        {
            ("practice submission", id => c => c.GetAsync($"/api/practice/submissions/{id}"), practiceSubId),
            ("submission", id => c => c.GetAsync($"/api/submissions/{id}"), submissionId),
            ("hide submission", id => c => c.PatchAsJsonAsync($"/api/submissions/{id}", new { hiddenByStudent = true }), submissionId),
            ("problem submissions", id => c => c.GetAsync($"/api/problems/{id}/submissions"), problemId),
            ("submit", id => c => c.PostAsJsonAsync($"/api/problems/{id}/submit", new { code = "int main(){}", language = "c" }), problemId),
            ("open post", id => c => c.PostAsync($"/api/problems/{id}/post", null), problemId),
            ("post note", id => c => c.PutAsJsonAsync($"/api/posts/{id}/note", new { note = "x" }), postId),
            ("post seen", id => c => c.PostAsync($"/api/posts/{id}/seen", null), postId),
            ("post reaction", id => c => c.PostAsJsonAsync($"/api/posts/{id}/reactions", new { emoji = "👍" }), postId),
        };
        foreach (var (name, call, real) in cases)
        {
            var existing = await Code(call(real));
            var missing = await Code(call(999_999));
            Assert.True(existing == missing && existing == HttpStatusCode.NotFound, $"{name}: existing id {existing}, missing id {missing}");
        }
    }
}
