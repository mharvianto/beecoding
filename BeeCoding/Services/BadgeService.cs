using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

public record BadgeDto(string Key, string Name, string Description, string Group, int Target, int Value, bool Earned, DateTime? EarnedAt);

/// <summary>
/// Practice badges. The rules are code (below), the earned ones are rows (<see cref="UserBadge"/>): once earned a badge stays, with the day it
/// was earned, even if the data behind it changes later. Every number comes from what the XP system already keeps: solve records (each distinct
/// problem once, board and practice together, Journey boss bonuses left out), the user's best streak and best day, and their level.
/// Names and descriptions are English here and translated in the browser (<c>tr()</c>).
/// </summary>
public sealed class BadgeService(AppDbContext db, ILogger<BadgeService> log)
{
    private record Stats(int Solved, int Medium, int Hard, int LongestStreak, int MaxDay, int Level);
    private record Def(string Key, string Name, string Description, string Group, int Target, Func<Stats, int> Value);

    public const string BossKeyPrefix = "journey-boss:";

    private static readonly Def[] Defs =
    [
        new("first_solve", "First Solve", "Solve your first problem", "Problems solved", 1, s => s.Solved),
        new("solver_10", "Getting Started", "Solve 10 problems", "Problems solved", 10, s => s.Solved),
        new("solver_50", "Problem Solver", "Solve 50 problems", "Problems solved", 50, s => s.Solved),
        new("solver_100", "Centurion", "Solve 100 problems", "Problems solved", 100, s => s.Solved),
        new("streak_3", "On a Roll", "Practice 3 days in a row", "Streaks", 3, s => s.LongestStreak),
        new("streak_7", "Week Warrior", "Practice 7 days in a row", "Streaks", 7, s => s.LongestStreak),
        new("streak_30", "Month Master", "Practice 30 days in a row", "Streaks", 30, s => s.LongestStreak),
        new("medium_10", "Medium Master", "Solve 10 Medium problems", "Difficulty", 10, s => s.Medium),
        new("hard_5", "Hard Hitter", "Solve 5 Hard problems", "Difficulty", 5, s => s.Hard),
        new("day_5", "Power Day", "Solve 5 problems in one day", "Intensity", 5, s => s.MaxDay),
        new("level_5", "Level 5", "Reach level 5", "Levels", 5, s => s.Level),
        new("level_10", "Level 10", "Reach level 10", "Levels", 10, s => s.Level),
    ];

    private async Task<Stats> StatsAsync(int userId, CancellationToken ct)
    {
        var records = db.SolveRecords.Where(r => r.UserId == userId && !r.ProblemKey.StartsWith(BossKeyPrefix));
        var u = await db.Users.Where(x => x.Id == userId).Select(x => new { x.Xp, x.LongestStreak, x.MaxSolvedInADay }).FirstOrDefaultAsync(ct);
        return new Stats(
            await records.CountAsync(ct),
            await records.CountAsync(r => r.Level == ProblemLevel.Medium, ct),
            await records.CountAsync(r => r.Level == ProblemLevel.Hard, ct),
            u?.LongestStreak ?? 0, u?.MaxSolvedInADay ?? 0, ProgressService.LevelForXp(u?.Xp ?? 0));
    }

    /// <summary>Awards every badge the user now qualifies for and returns the new keys. <paramref name="silent"/> marks them already seen
    /// (the one-time backfill, so nobody is greeted by a pile of toasts). Never throws: a badge must not break grading.</summary>
    public async Task<List<string>> EvaluateAsync(int userId, bool silent = false, CancellationToken ct = default)
    {
        var added = new List<string>();
        try
        {
            var stats = await StatsAsync(userId, ct);
            var have = (await db.UserBadges.Where(b => b.UserId == userId).Select(b => b.Key).ToListAsync(ct)).ToHashSet();
            var now = DateTime.UtcNow;
            foreach (var d in Defs.Where(d => !have.Contains(d.Key) && d.Value(stats) >= d.Target))
            {
                db.UserBadges.Add(new UserBadge { UserId = userId, Key = d.Key, EarnedAt = now, SeenAt = silent ? now : null });
                added.Add(d.Key);
            }
            if (added.Count > 0)
            {
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); added.Clear(); }   // a parallel grade lost the race on the unique index: the other one awarded it
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Badge check failed for user #{Id}", userId);
            added.Clear();
        }
        return added;
    }

    public async Task<List<BadgeDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        await EvaluateAsync(userId, ct: ct);
        var stats = await StatsAsync(userId, ct);
        var earned = await db.UserBadges.Where(b => b.UserId == userId).ToDictionaryAsync(b => b.Key, b => b.EarnedAt, ct);
        return Defs.Select(d => new BadgeDto(d.Key, d.Name, d.Description, d.Group, d.Target, Math.Min(d.Value(stats), d.Target),
            earned.ContainsKey(d.Key), earned.TryGetValue(d.Key, out var at) ? at : null)).ToList();
    }

    /// <summary>The badges earned since the browser last asked; marks them seen, so each one is announced once.</summary>
    public async Task<List<BadgeDto>> TakeNewAsync(int userId, CancellationToken ct = default)
    {
        var fresh = await db.UserBadges.Where(b => b.UserId == userId && b.SeenAt == null).ToListAsync(ct);
        if (fresh.Count == 0) return [];
        var now = DateTime.UtcNow;
        foreach (var b in fresh) b.SeenAt = now;
        await db.SaveChangesAsync(ct);
        return fresh.Join(Defs, b => b.Key, d => d.Key, (b, d) => new BadgeDto(d.Key, d.Name, d.Description, d.Group, d.Target, d.Target, true, b.EarnedAt)).ToList();
    }
}

/// <summary>Once, at startup: gives existing users the badges they already qualify for, silently. Only users with solves and no badge row yet, so it is cheap and safe to repeat.</summary>
public sealed class BadgeBackfill(IServiceScopeFactory scopes, ILogger<BadgeBackfill> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var badges = scope.ServiceProvider.GetRequiredService<BadgeService>();
            var users = await db.Users.Where(u => u.DeletedAt == null && u.Xp > 0 && !db.UserBadges.Any(b => b.UserId == u.Id)).Select(u => u.Id).ToListAsync(stoppingToken);
            var n = 0;
            foreach (var id in users) n += (await badges.EvaluateAsync(id, silent: true, ct: stoppingToken)).Count;
            if (n > 0) log.LogInformation("Badge backfill gave {Count} badge(s) to {Users} existing user(s)", n, users.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogWarning(ex, "Badge backfill failed"); }
    }
}
