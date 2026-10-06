using System.Collections.Concurrent;
using System.Security.Claims;
using BeeCoding.Data;
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
}
