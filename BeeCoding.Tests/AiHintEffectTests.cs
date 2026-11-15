using BeeCoding.Services.Ai;
using Xunit;

namespace BeeCoding.Tests;

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
}
