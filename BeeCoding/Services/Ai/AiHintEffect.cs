using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services.Ai;

public record HintEvt(int UserId, string Key, DateTime At, int Level);
public record SubEvt(int UserId, string Key, DateTime At, bool Accepted);

/// <summary>
/// "Are AI hints helping?" from the hint log and submissions. Per (student, problem) that asked for a hint:
/// did an Accepted follow it, how fast, and who went deep (level 3+) and still never solved it.
/// Pairs already Accepted before their first hint are left out (the hint was not what they were stuck on).
/// The baseline is students with submissions on the same problems but no hint. It is NOT a controlled
/// comparison: students ask for hints on the harder problems, so read the speed and the "within 30 min" numbers first.
/// </summary>
public static class AiHintEffect
{
    public static readonly TimeSpan Soon = TimeSpan.FromMinutes(30);
    public const int DeepLevel = 3;

    public record Result(int Pairs, int Students, double? SolvedAfterPct, double? SolvedWithin30MinPct,
        double? MedianMinutesToSolve, int DeepStuck, int BaselinePairs, double? BaselineSolvedPct);

    public static Result Compute(IReadOnlyList<HintEvt> hints, IReadOnlyList<SubEvt> subs)
    {
        var subsByPair = subs.ToLookup(s => (s.UserId, s.Key));
        var hintPairs = hints.GroupBy(h => (h.UserId, h.Key))
            .Select(g => (g.Key.UserId, g.Key.Key, First: g.Min(h => h.At), Max: g.Max(h => h.Level)))
            .Where(p => !subsByPair[(p.UserId, p.Key)].Any(s => s.Accepted && s.At < p.First))
            .ToList();

        var solveTimes = new List<double>();
        int solved = 0, soon = 0, deepStuck = 0;
        foreach (var p in hintPairs)
        {
            var first = subsByPair[(p.UserId, p.Key)].Where(s => s.Accepted && s.At >= p.First).OrderBy(s => s.At).FirstOrDefault();
            if (first is null) { if (p.Max >= DeepLevel) deepStuck++; continue; }
            solved++;
            var took = first.At - p.First;
            solveTimes.Add(took.TotalMinutes);
            if (took <= Soon) soon++;
        }

        var hinted = hints.Select(h => (h.UserId, h.Key)).ToHashSet();
        var hintedKeys = hintPairs.Select(p => p.Key).ToHashSet();
        var baseline = subs.Where(s => hintedKeys.Contains(s.Key) && !hinted.Contains((s.UserId, s.Key)))
            .GroupBy(s => (s.UserId, s.Key)).ToList();

        double? Pct(int n, int d) => d == 0 ? null : Math.Round(100.0 * n / d, 1);
        solveTimes.Sort();
        double? median = solveTimes.Count == 0 ? null
            : Math.Round(solveTimes.Count % 2 == 1 ? solveTimes[solveTimes.Count / 2]
                : (solveTimes[solveTimes.Count / 2 - 1] + solveTimes[solveTimes.Count / 2]) / 2, 1);

        return new Result(hintPairs.Count, hintPairs.Select(p => p.UserId).Distinct().Count(),
            Pct(solved, hintPairs.Count), Pct(soon, hintPairs.Count), median, deepStuck,
            baseline.Count, Pct(baseline.Count(g => g.Any(s => s.Accepted)), baseline.Count));
    }

    /// <summary>
    /// Read the hint log and submissions for a window and compute. With <paramref name="organizationId"/> only that organization's board
    /// problems count (practice problems belong to a user, not an organization, so they are left out).
    /// </summary>
    public static async Task<Result> LoadAsync(AppDbContext db, DateTime since, int? organizationId = null)
    {
        HashSet<string>? orgKeys = null;
        if (organizationId is int org)
            orgKeys = (await db.Problems.Where(p => p.Board!.OrganizationId == org).Select(p => p.Id).ToListAsync()).Select(id => $"board:{id}").ToHashSet();

        var all = await db.AiHintEvents.Where(h => h.At >= since && (h.ProblemKey.StartsWith("board:") || (orgKeys == null && h.ProblemKey.StartsWith("bank:"))))
            .Select(h => new HintEvt(h.UserId, h.ProblemKey, h.At, h.Level)).ToListAsync();
        var hints = orgKeys is null ? all : all.Where(h => orgKeys.Contains(h.Key)).ToList();
        if (hints.Count == 0) return Compute(hints, []);

        var boardIds = hints.Where(h => h.Key.StartsWith("board:")).Select(h => int.Parse(h.Key[6..])).Distinct().ToList();
        var bankIds = hints.Where(h => h.Key.StartsWith("bank:")).Select(h => int.Parse(h.Key[5..])).Distinct().ToList();
        var subs = new List<SubEvt>();
        // Before the window too: an Accepted from before the first hint must be seen to leave that pair out.
        subs.AddRange((await db.Submissions.Where(s => boardIds.Contains(s.ProblemId) && s.Status == SubmissionStatus.Done)
            .Select(s => new { s.UserId, s.ProblemId, s.CreatedAt, s.Verdict }).ToListAsync())
            .Select(s => new SubEvt(s.UserId, $"board:{s.ProblemId}", s.CreatedAt, s.Verdict == Verdict.Accepted)));
        if (bankIds.Count > 0)
            subs.AddRange((await db.BankSubmissions.Where(s => bankIds.Contains(s.BankProblemId) && s.Status == SubmissionStatus.Done)
                .Select(s => new { s.UserId, s.BankProblemId, s.CreatedAt, s.Verdict }).ToListAsync())
                .Select(s => new SubEvt(s.UserId, $"bank:{s.BankProblemId}", s.CreatedAt, s.Verdict == Verdict.Accepted)));
        return Compute(hints, subs);
    }
}
