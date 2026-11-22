using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AiUsageDayTests
{
    [Fact]
    public async Task Dashboard_today_and_month_follow_each_users_own_calendar_day()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // far east of UTC: his local day is already the next one for 4 hours out of every 24 (+14h), so use the extreme offset
        var east = new User { Email = "east@t.dev", DisplayName = "E", UtcOffsetMinutes = 14 * 60 };
        var west = new User { Email = "west@t.dev", DisplayName = "W", UtcOffsetMinutes = -12 * 60 };
        db.Users.AddRange(east, west);
        await db.SaveChangesAsync();
        var eastToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14));
        var westToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-12));
        db.AiUsages.AddRange(
            new AiUsage { UserId = east.Id, Day = eastToday, Calls = 3, PromptTokens = 10, CompletionTokens = 5 },
            new AiUsage { UserId = west.Id, Day = westToday, Calls = 2, PromptTokens = 4, CompletionTokens = 1 },
            // yesterday for the east user: this month at most, never today
            new AiUsage { UserId = east.Id, Day = eastToday.AddDays(-1), Calls = 7, PromptTokens = 1, CompletionTokens = 1 });
        await db.SaveChangesAsync();

        var (today, month) = await UserClock.AiTodayAndMonthAsync(db, db.AiUsages);
        Assert.Equal(5, today.Calls);                       // 3 + 2, whatever the UTC date is
        Assert.Equal(5 + (eastToday.Day > 1 ? 7 : 0), month.Calls);   // yesterday counts only when it is still this month
    }
}
