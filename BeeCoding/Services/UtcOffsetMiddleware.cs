using System.Collections.Concurrent;
using System.Security.Claims;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>Stores the browser's UTC offset (<c>X-UTC-Offset</c>, minutes) on the signed-in user when it changes,
/// so server-side code can compute that user's local calendar day (see <see cref="UserClock"/>). The last value
/// seen per user is cached in memory, so a request costs a dictionary lookup, and the database is written only
/// when the offset actually changes (a new device or time zone, or a daylight-saving switch).</summary>
public sealed class UtcOffsetMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<int, int> Seen = new();

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && int.TryParse(ctx.Request.Headers["X-UTC-Offset"], out var offset) && offset is >= -840 and <= 840
            && int.TryParse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            && !(Seen.TryGetValue(userId, out var known) && known == offset))
        {
            await db.Users.Where(u => u.Id == userId && u.UtcOffsetMinutes != offset)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.UtcOffsetMinutes, offset));
            Seen[userId] = offset;
        }
        await next(ctx);
    }
}

/// <summary>A user's local calendar day, from the offset <see cref="UtcOffsetMiddleware"/> last stored.</summary>
public static class UserClock
{
    public static async Task<DateOnly> LocalTodayAsync(AppDbContext db, int userId, CancellationToken ct = default)
    {
        var offset = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.UtcOffsetMinutes).FirstOrDefaultAsync(ct);
        return DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(offset));
    }

    /// <summary>The viewer's local "now", for an axis of AiUsage days (those are each user's own calendar day).</summary>
    public static async Task<DateTime> LocalNowAsync(AppDbContext db, int userId, CancellationToken ct = default)
    {
        var offset = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.UtcOffsetMinutes).FirstOrDefaultAsync(ct);
        return DateTime.UtcNow.AddMinutes(offset);
    }

    /// <summary>"Today" and "this month" over the given usage rows, each row judged against ITS user's own calendar day
    /// (<c>AiUsage.Day</c> is that day), like the AI-usage page. Comparing with the UTC day makes the card show yesterday's
    /// total for the first hours of every local day east of UTC, and a month that ends early or late.</summary>
    public static async Task<(AdminAiUsageBucket Today, AdminAiUsageBucket Month)> AiTodayAndMonthAsync(AppDbContext db, IQueryable<AiUsage> rows, CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-45);   // covers any user's month start, whatever their offset
        var list = await rows.Where(x => x.Day >= from).ToListAsync(ct);
        var ids = list.Select(x => x.UserId).Distinct().ToList();
        var offsets = await db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UtcOffsetMinutes, ct);
        int tc = 0, mc = 0; long tp = 0, tk = 0, mp = 0, mk = 0;
        foreach (var x in list)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(offsets.GetValueOrDefault(x.UserId)));
            if (x.Day < new DateOnly(today.Year, today.Month, 1) || x.Day > today) continue;
            mc += x.Calls; mp += x.PromptTokens; mk += x.CompletionTokens;
            if (x.Day == today) { tc += x.Calls; tp += x.PromptTokens; tk += x.CompletionTokens; }
        }
        return (new(tc, tp, tk, tp + tk), new(mc, mp, mk, mp + mk));
    }
}
