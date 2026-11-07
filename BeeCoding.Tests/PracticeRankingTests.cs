using BeeCoding.Models;
using BeeCoding.Services;
using Xunit;

namespace BeeCoding.Tests;

public class PracticeRankingTests
{
    [Fact]
    public void A_level_never_overtakes_the_next_one()
    {
        // the easiest possible Medium (every attempt accepted) still sorts after the hardest possible Easy (none accepted)
        Assert.True(PracticeRanking.Key(ProblemLevel.Easy, 1000, 0) < PracticeRanking.Key(ProblemLevel.Medium, 1000, 1));
        Assert.True(PracticeRanking.Key(ProblemLevel.Medium, 1000, 0) < PracticeRanking.Key(ProblemLevel.Hard, 1000, 1));
    }

    [Fact]
    public void Within_a_level_a_higher_ac_rate_comes_first()
    {
        Assert.True(PracticeRanking.Key(ProblemLevel.Medium, 200, 0.8) < PracticeRanking.Key(ProblemLevel.Medium, 200, 0.2));
    }

    [Fact]
    public void One_lucky_submission_does_not_beat_a_well_tried_problem()
    {
        // 1 of 1 accepted (raw rate 100%) must not rank above 180 of 200 (90%)
        Assert.True(PracticeRanking.Key(ProblemLevel.Medium, 200, 0.9) < PracticeRanking.Key(ProblemLevel.Medium, 1, 1.0));
    }

    [Fact]
    public void An_untried_problem_sits_at_its_levels_typical_spot()
    {
        var untried = PracticeRanking.Key(ProblemLevel.Hard, 0, 0);
        Assert.True(PracticeRanking.Key(ProblemLevel.Hard, 500, 0.5) < untried);   // easier than typical
        Assert.True(untried < PracticeRanking.Key(ProblemLevel.Hard, 500, 0.05));  // harder than typical
    }
}
