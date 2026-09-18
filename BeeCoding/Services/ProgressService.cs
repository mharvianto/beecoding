using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

public record ProgressDto(int Xp, int Level, int LevelStartXp, int NextLevelXp, int SolvedCount, int Streak, int SolvedToday,
    int LongestStreak = 0, int MaxSolvedInADay = 0);

/// <summary>
/// XP / leveling. XP is awarded once per distinct problem the first time a student
/// fully solves it (Accepted, score 1.0). A bank problem and its board copies share
/// one key, so the same problem can't be farmed across boards.
/// </summary>
public class ProgressService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public static int XpFor(ProblemLevel level) => level switch
    {
        ProblemLevel.Easy => 10,
        ProblemLevel.Hard => 40,
        _ => 20,
    };

    // Level L needs 25*L*(L-1) total XP: L1=0, L2=50, L3=150, L4=300, L5=500 ...
    public static int LevelStartXp(int level) => 25 * level * (level - 1);

    public static int LevelForXp(int xp) =>
        Math.Max(1, (int)Math.Floor((25 + Math.Sqrt(625 + 100.0 * Math.Max(0, xp))) / 50));

    public static string BankKey(int bankProblemId) => $"bank:{bankProblemId}";

    public static string KeyForBoardProblem(Problem p) =>
        p.SourceBankProblemId is int src ? BankKey(src) : $"board:{p.Id}";

    /// <summary>
    /// Record a solve if it's the user's first for this problem key; returns XP added (0 if already solved).
    /// </summary>
    public async Task<int> AwardSolveAsync(int userId, string problemKey, ProblemLevel level, CancellationToken ct = default)
    {
        if (await _db.SolveRecords.AnyAsync(r => r.UserId == userId && r.ProblemKey == problemKey, ct))
            return 0;

        int xp = XpFor(level);
        _db.SolveRecords.Add(new SolveRecord
        {
            UserId = userId,
            ProblemKey = problemKey,
            Level = level,
            XpAwarded = xp,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // lost the race on the unique index — someone else recorded it first
            return 0;
        }

        await _db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Xp, u => u.Xp + xp), ct);
        return xp;
    }

    /// <summary>
    /// Bumps the daily practice streak for a fresh Accepted solve. <paramref name="localDay"/>
    /// is the client's local calendar day (captured at submit time), not server UTC — a
    /// streak day means "the student's day", so it can't be computed from CreatedAt alone.
    /// Consecutive days increment; a gap (or the same day again) restarts/no-ops.
    /// </summary>
    public async Task UpdateStreakAsync(int userId, DateOnly localDay, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        if (user.StreakLocalDay is not DateOnly last)
            user.CurrentStreak = 1;
        else if (last == localDay)
            return;   // already counted today
        else if (last.AddDays(1) == localDay)
            user.CurrentStreak++;
        else
            user.CurrentStreak = 1;   // missed a day (or clock skew) — restart

        user.StreakLocalDay = localDay;
        if (user.CurrentStreak > user.LongestStreak) user.LongestStreak = user.CurrentStreak;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Refreshes the personal-best "most solved in one day" record after a fresh
    /// Accepted solve (board or practice — see CountSolvedOnLocalDayAsync's combined
    /// definition). Called from both grading paths, unlike UpdateStreakAsync which is
    /// practice-only by design.</summary>
    public async Task UpdateMaxSolvedInADayAsync(int userId, DateOnly localDay, CancellationToken ct = default)
    {
        var today = await CountSolvedOnLocalDayAsync(userId, localDay, ct);
        await _db.Users.Where(u => u.Id == userId && u.MaxSolvedInADay < today)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MaxSolvedInADay, today), ct);
    }

    /// <summary>
    /// <paramref name="localDay"/> is the CALLER's current local day (from the client making
    /// this request), used to (1) detect a streak that's gone stale since the user's last
    /// solve — the stored CurrentStreak isn't reset until their next solve, so without this
    /// check a broken streak would still display as alive until then — and (2) count today's
    /// Accepted solves (board + practice combined) for the celebration toast. Left at 0 if
    /// omitted, since without it "today" is undefined.
    /// </summary>
    public async Task<ProgressDto> GetAsync(int userId, DateOnly? localDay = null, CancellationToken ct = default)
    {
        var user = await _db.Users.Where(u => u.Id == userId)
            .Select(u => new { u.Xp, u.CurrentStreak, u.StreakLocalDay, u.LongestStreak, u.MaxSolvedInADay }).FirstOrDefaultAsync(ct);
        var solved = await _db.SolveRecords.CountAsync(r => r.UserId == userId, ct);
        int level = LevelForXp(user?.Xp ?? 0);

        int streak = user?.CurrentStreak ?? 0;
        if (streak > 0 && localDay is DateOnly today && user?.StreakLocalDay is DateOnly last
            && today.DayNumber - last.DayNumber > 1)
            streak = 0;

        int solvedToday = localDay is DateOnly day ? await CountSolvedOnLocalDayAsync(userId, day, ct) : 0;

        return new ProgressDto(user?.Xp ?? 0, level, LevelStartXp(level), LevelStartXp(level + 1), solved, streak, solvedToday,
            user?.LongestStreak ?? 0, user?.MaxSolvedInADay ?? 0);
    }

    /// <summary>Accepted, fully-scored solves (board + practice combined, repeats included —
    /// this counts solve EVENTS, not distinct problems) on one of the student's local days.
    /// Powers the "N solved today" celebration toast.</summary>
    public async Task<int> CountSolvedOnLocalDayAsync(int userId, DateOnly localDay, CancellationToken ct = default)
    {
        var board = await _db.Submissions.CountAsync(
            s => s.UserId == userId && s.LocalDay == localDay && s.Verdict == Verdict.Accepted && s.Score >= 1.0, ct);
        var practice = await _db.BankSubmissions.CountAsync(
            s => s.UserId == userId && s.LocalDay == localDay && s.Verdict == Verdict.Accepted && s.Score >= 1.0, ct);
        return board + practice;
    }
}
