using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SubmissionCodeJanitorTests
{
    [Fact]
    public async Task Old_finished_code_is_blanked_but_the_verdict_stays_and_young_or_queued_or_disabled_ones_are_left_alone()
    {
        using var app = new TestApp();
        app.CreateClient();   // boots the app: migrates and seeds
        var runtime = app.Services.GetRequiredService<PlatformRuntimeConfig>();
        var old = DateTime.UtcNow.AddDays(-400);
        int oldDone, young, oldQueued, practiceOld;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var problem = await db.Problems.FirstAsync();
            var user = await db.Users.FirstAsync();
            var bank = new BankProblem { OwnerId = user.Id, Title = "B", StatementMarkdown = "s", IsPublic = true };
            db.BankProblems.Add(bank);
            Submission S(DateTime at, SubmissionStatus st) => new() { ProblemId = problem.Id, UserId = user.Id, Code = "secret();", CompilerOutput = "warn", FailedTest = "t", Status = st, Verdict = Verdict.Accepted, Score = 1, CreatedAt = at };
            var a = S(old, SubmissionStatus.Done); var b = S(DateTime.UtcNow, SubmissionStatus.Done); var c = S(old, SubmissionStatus.Queued);
            var p = new BankSubmission { BankProblem = bank, UserId = user.Id, Code = "secret();", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, Score = 1, CreatedAt = old };
            db.Submissions.AddRange(a, b, c); db.BankSubmissions.Add(p);
            await db.SaveChangesAsync();
            (oldDone, young, oldQueued, practiceOld) = (a.Id, b.Id, c.Id, p.Id);
        }
        var janitor = new SubmissionCodeJanitor(app.Services.GetRequiredService<IServiceScopeFactory>(), runtime, NullLogger<SubmissionCodeJanitor>.Instance);

        runtime.SetCodeRetention(0, 0);               // off: nothing happens
        await janitor.SweepAsync(CancellationToken.None);
        using (var scope = app.Services.CreateScope())
            Assert.Equal("secret();", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Submissions.FindAsync(oldDone))!.Code);

        runtime.SetCodeRetention(365, 0);             // board on, practice still off
        await janitor.SweepAsync(CancellationToken.None);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = (await db.Submissions.FindAsync(oldDone))!;
            Assert.Equal("", a.Code); Assert.Equal("", a.CompilerOutput); Assert.Null(a.FailedTest); Assert.NotNull(a.CodePurgedAt);
            Assert.Equal(Verdict.Accepted, a.Verdict); Assert.Equal(1, a.Score);            // the grade is untouched
            Assert.Null(Mapping.ToDto(a, a.UserId, canSeeCode: true, "x").Code);
            Assert.True(Mapping.ToDto(a, a.UserId, canSeeCode: true, "x").CodePurged);
            Assert.Equal("secret();", (await db.Submissions.FindAsync(young))!.Code);       // too young
            Assert.Equal("secret();", (await db.Submissions.FindAsync(oldQueued))!.Code);   // not finished: JudgeRecovery may still need it
            Assert.Equal("secret();", (await db.BankSubmissions.FindAsync(practiceOld))!.Code);
        }

        runtime.SetCodeRetention(365, 180);
        await janitor.SweepAsync(CancellationToken.None);
        using (var scope = app.Services.CreateScope())
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<AppDbContext>().BankSubmissions.FindAsync(practiceOld))!.CodePurgedAt);
    }
}
