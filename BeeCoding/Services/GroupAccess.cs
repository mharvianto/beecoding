using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Single source of truth for what a problem's group means for students: whether they can see
/// it (hidden flags + open time), whether it has closed for submissions, and its effective
/// exam mode. Callers must load <c>Problem.Group</c> (Include) — an unloaded group on a
/// grouped problem fails closed rather than silently behaving as "ungrouped".
/// </summary>
public static class GroupAccess
{
    public static bool StudentVisible(Problem p, DateTime now)
    {
        if (p.Hidden) return false;
        if (p.GroupId is null) return true;
        var g = p.Group;
        return g is not null && !g.Hidden && !(g.OpensAt > now);
    }

    public static bool IsClosed(Problem p, DateTime now) => p.Group?.ClosesAt <= now;

    /// <summary>A grouped problem uses its group's exam mode; an ungrouped one the board's.</summary>
    public static bool ExamMode(Board board, Problem p) => p.Group?.ExamMode ?? board.ExamMode;

    /// <summary>Same as <see cref="ExamMode"/> for a problem known only by id (a deleted
    /// problem falls back to the board's setting).</summary>
    public static async Task<bool> ExamModeAsync(AppDbContext db, Board board, int problemId)
    {
        var groupExam = await db.Problems.Where(p => p.Id == problemId)
            .Select(p => p.Group != null ? (bool?)p.Group.ExamMode : null).FirstOrDefaultAsync();
        return groupExam ?? board.ExamMode;
    }

    public static string State(ProblemGroup g, DateTime now) =>
        g.OpensAt > now ? "Upcoming" : g.ClosesAt <= now ? "Closed" : "Open";

    public static string? Validate(UpsertProblemGroupDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return "A group needs a title.";
        if (dto.OpensAt is { } o && dto.ClosesAt is { } c && c <= o) return "The close time must be after the open time.";
        return null;
    }

    public static void Apply(ProblemGroup g, UpsertProblemGroupDto dto)
    {
        var title = dto.Title.Trim();
        g.Title = title.Length > 120 ? title[..120] : title;
        g.Hidden = dto.Hidden;
        g.ExamMode = dto.ExamMode;
        g.OpensAt = dto.OpensAt?.ToUniversalTime();
        g.ClosesAt = dto.ClosesAt?.ToUniversalTime();
    }
}
