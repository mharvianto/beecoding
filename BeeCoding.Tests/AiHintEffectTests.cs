using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AiHintEffectTests
{
    static readonly DateTime T = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Counts_solves_after_hint_speed_deep_stuck_and_baseline()
    {
        var hints = new List<HintEvt>
        {
            new(1, "board:5", T, 1),                       // solves 10 min later
            new(2, "board:5", T, 1), new(2, "board:5", T.AddMinutes(5), 3),   // never solves, deep
            new(3, "board:5", T, 1),                       // already solved before the hint: left out
            new(4, "board:5", T, 1),                       // solves 3 h later
        };
        var subs = new List<SubEvt>
        {
            new(1, "board:5", T.AddMinutes(10), true),
            new(2, "board:5", T.AddMinutes(8), false),
            new(3, "board:5", T.AddMinutes(-5), true),
            new(4, "board:5", T.AddHours(3), true),
            new(9, "board:5", T, false),                   // baseline, no hint, not solved
            new(8, "board:5", T, true),                    // baseline, solved
            new(7, "board:6", T, true),                    // other problem: not in baseline
        };

        var r = AiHintEffect.Compute(hints, subs);

        Assert.Equal(3, r.Pairs);
        Assert.Equal(3, r.Students);
        Assert.Equal(66.7, r.SolvedAfterPct);
        Assert.Equal(33.3, r.SolvedWithin30MinPct);
        Assert.Equal(95, r.MedianMinutesToSolve);          // 10 and 180 min
        Assert.Equal(1, r.DeepStuck);
        Assert.Equal(2, r.BaselinePairs);
        Assert.Equal(50, r.BaselineSolvedPct);
    }

    [Fact]
    public void Empty_gives_null_percentages()
    {
        var r = AiHintEffect.Compute([], []);
        Assert.Equal(0, r.Pairs);
        Assert.Null(r.SolvedAfterPct);
        Assert.Null(r.MedianMinutesToSolve);
    }

    [Fact]
    public async Task An_organization_only_sees_hints_on_its_own_boards()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
        var org = new Organization { Name = "Org", Slug = "org-x" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var mine = new Board { Title = "In org", OwnerId = owner.Id, OrganizationId = org.Id, JoinCode = "ORG111" };
        var other = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
        db.Boards.Add(mine);
        await db.SaveChangesAsync();
        var pMine = new Problem { BoardId = mine.Id, Title = "A", StatementMarkdown = "x" };
        var pOther = new Problem { BoardId = other.Id, Title = "B", StatementMarkdown = "x" };
        db.Problems.AddRange(pMine, pOther);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.AiHintEvents.AddRange(
            new AiHintEvent { UserId = owner.Id, ProblemKey = $"board:{pMine.Id}", Level = 1, At = now.AddMinutes(-20) },
            new AiHintEvent { UserId = owner.Id, ProblemKey = $"board:{pOther.Id}", Level = 1, At = now.AddMinutes(-20) },
            new AiHintEvent { UserId = owner.Id, ProblemKey = "bank:1", Level = 1, At = now.AddMinutes(-20) });
        db.Submissions.Add(new Submission { ProblemId = pMine.Id, UserId = owner.Id, Code = "x", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, CreatedAt = now.AddMinutes(-10) });
        await db.SaveChangesAsync();

        var inOrg = await AiHintEffect.LoadAsync(db, now.AddDays(-1), org.Id);
        Assert.Equal(1, inOrg.Pairs);
        Assert.Equal(100, inOrg.SolvedAfterPct);
        var platform = await AiHintEffect.LoadAsync(db, now.AddDays(-1));
        Assert.Equal(3, platform.Pairs);
    }
}
