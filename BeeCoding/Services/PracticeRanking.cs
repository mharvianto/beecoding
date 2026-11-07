using BeeCoding.Models;

namespace BeeCoding.Services;

/// <summary>
/// Default order of the Practice list: level first, then the easiest problem first inside a level.
/// "Easy" is the full-AC rate pulled toward what its level normally gets, so a problem nobody has tried
/// sits at its level's typical spot and one with plenty of submissions moves to its real rate.
/// </summary>
public static class PracticeRanking
{
    // shortcut: guessed typical AC rates and weight, not measured on the production bank; tune once real submissions exist.
    private static readonly double[] LevelPrior = [0.60, 0.35, 0.15];   // Easy, Medium, Hard
    private const double PriorWeight = 10;                              // submissions' worth of trust in the prior

    /// <summary>Smaller sorts first. The level is the whole part, so a level never overtakes the next one.</summary>
    public static double Key(ProblemLevel level, int submissions, double acRate)
    {
        var prior = LevelPrior[Math.Clamp((int)level - 1, 0, LevelPrior.Length - 1)];
        var easy = (acRate * submissions + PriorWeight * prior) / (submissions + PriorWeight);
        return (int)level + (1 - easy) * 0.99;
    }
}
