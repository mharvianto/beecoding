using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

public record LeaderRowDto(int Rank, int UserId, string DisplayName, string Role, int Xp, int Level, bool Me);

[ApiController]
[Authorize]
public class ProgressController(AppDbContext db, ProgressService progress) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly ProgressService _progress = progress;

    [HttpGet("api/me/progress")]
    public Task<ProgressDto> Mine() => _progress.GetAsync(UserId);

    /// <summary>Personal dashboard: progress aggregated across every board the user has
    /// joined plus practice/bank activity — not scoped to any one board (see
    /// BoardsController.Stats for the teacher's per-board equivalent).</summary>
    [HttpGet("api/me/dashboard")]
    public async Task<ActionResult<StudentDashboardDto>> Dashboard()
    {
        var p = await _progress.GetAsync(UserId);
        var rankedUsers = await _db.Users.CountAsync(u => u.Xp > 0);
        var higher = await _db.Users.CountAsync(u => u.Xp > p.Xp);
        var rank = p.Xp > 0 ? higher + 1 : 0;
        var boardsJoined = await _db.BoardMemberships.CountAsync(m => m.UserId == UserId);

        var boardVerdicts = await _db.Submissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score }).ToListAsync();
        var bankVerdicts = await _db.BankSubmissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score }).ToListAsync();
        var totalAttempts = boardVerdicts.Count + bankVerdicts.Count;
        var accepted = boardVerdicts.Count(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0)
                     + bankVerdicts.Count(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0);

        return new StudentDashboardDto(p.Xp, p.Level, p.LevelStartXp, p.NextLevelXp, p.SolvedCount,
            rank, rankedUsers, boardsJoined, totalAttempts, accepted);
    }

    /// <summary>Weekly attempts + solves (first-time accepts, matching XP awards) across
    /// every board + practice/bank.</summary>
    [HttpGet("api/me/dashboard/weekly")]
    public async Task<ActionResult<List<MyWeeklyStatDto>>> DashboardWeekly([FromQuery] int weeks = 12)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        var boardDates = await _db.Submissions.Where(s => s.UserId == UserId).Select(s => s.CreatedAt).ToListAsync();
        var bankDates = await _db.BankSubmissions.Where(s => s.UserId == UserId).Select(s => s.CreatedAt).ToListAsync();
        var solveDates = await _db.SolveRecords.Where(s => s.UserId == UserId).Select(s => s.CreatedAt).ToListAsync();

        DateOnly WeekStart(DateTime dt)
        {
            var d = DateOnly.FromDateTime(dt);
            return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        }
        var attemptsByWeek = boardDates.Concat(bankDates).GroupBy(WeekStart).ToDictionary(g => g.Key, g => g.Count());
        var solvedByWeek = solveDates.GroupBy(WeekStart).ToDictionary(g => g.Key, g => g.Count());

        var weekKeys = attemptsByWeek.Keys.Union(solvedByWeek.Keys)
            .OrderByDescending(k => k).Take(weeks).OrderBy(k => k);

        return weekKeys.Select(k => new MyWeeklyStatDto(k.ToString("yyyy-MM-dd"),
            attemptsByWeek.GetValueOrDefault(k), solvedByWeek.GetValueOrDefault(k))).ToList();
    }

    /// <summary>Topics ordered by lowest accept rate first ("what you're struggling with"),
    /// across board submissions + bank practice submissions.</summary>
    [HttpGet("api/me/dashboard/topics")]
    public async Task<ActionResult<List<AdminTopicStatDto>>> DashboardTopics([FromQuery] int take = 8)
    {
        take = Math.Clamp(take, 1, 50);
        var boardSubs = await _db.Submissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score, Tags = s.Problem!.Tags }).ToListAsync();
        var bankSubs = await _db.BankSubmissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score, Tags = s.BankProblem!.Tags }).ToListAsync();

        var byTag = new Dictionary<string, (int Attempts, int Accepted)>();
        static IEnumerable<string> TagsOf(string? t) =>
            (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(x => x.ToLowerInvariant()).Distinct();

        foreach (var s in boardSubs.Concat(bankSubs))
        {
            foreach (var tag in TagsOf(s.Tags))
            {
                var (attempts, acc) = byTag.TryGetValue(tag, out var v) ? v : (0, 0);
                attempts++;
                if (s.Verdict == Verdict.Accepted && s.Score >= 1.0) acc++;
                byTag[tag] = (attempts, acc);
            }
        }

        return byTag.Where(kv => kv.Value.Attempts > 0)
            .OrderBy(kv => kv.Value.Accepted / (double)kv.Value.Attempts)
            .ThenByDescending(kv => kv.Value.Attempts)
            .Take(take)
            .Select(kv => new AdminTopicStatDto(kv.Key, kv.Value.Attempts, kv.Value.Accepted,
                kv.Value.Accepted / (double)kv.Value.Attempts))
            .ToList();
    }

    [HttpGet("api/leaderboard")]
    public async Task<ActionResult<IEnumerable<LeaderRowDto>>> Leaderboard([FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var top = await _db.Users
            .Where(u => u.Xp > 0)
            .OrderByDescending(u => u.Xp).ThenBy(u => u.Id)
            .Take(limit)
            .Select(u => new { u.Id, u.DisplayName, u.Role, u.Xp })
            .ToListAsync();

        return top.Select((u, i) => new LeaderRowDto(
            i + 1, u.Id, u.DisplayName, u.Role.ToString(), u.Xp,
            ProgressService.LevelForXp(u.Xp), u.Id == UserId)).ToList();
    }
}
