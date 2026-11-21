using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class BadgeTests
{
    [Fact]
    public async Task Badges_follow_the_numbers_are_kept_once_earned_and_announced_once()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
        int id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.FirstAsync(x => x.Email == "kid@test.dev");
            id = u.Id;
            u.LongestStreak = 7; u.MaxSolvedInADay = 2; u.Xp = 160;      // level 3
            for (var i = 0; i < 10; i++) db.SolveRecords.Add(new SolveRecord { UserId = id, ProblemKey = $"bank:{i}", Level = i < 5 ? ProblemLevel.Medium : ProblemLevel.Easy });
            db.SolveRecords.Add(new SolveRecord { UserId = id, ProblemKey = "journey-boss:99", Level = ProblemLevel.Hard });   // a boss bonus is not a solved problem
            await db.SaveChangesAsync();
        }

        async Task<JsonElement> All() => await kid.GetFromJsonAsync<JsonElement>("/api/me/badges");
        bool Earned(JsonElement all, string key) => all.EnumerateArray().First(b => b.GetProperty("key").GetString() == key).GetProperty("earned").GetBoolean();

        var all = await All();
        Assert.True(Earned(all, "first_solve")); Assert.True(Earned(all, "solver_10"));          // 10 real solves (the boss does not count)
        Assert.False(Earned(all, "solver_50")); Assert.False(Earned(all, "medium_10"));          // only 5 Medium
        Assert.True(Earned(all, "streak_3")); Assert.True(Earned(all, "streak_7")); Assert.False(Earned(all, "streak_30"));
        Assert.False(Earned(all, "hard_5"));                                                      // the Hard boss record is excluded
        Assert.False(Earned(all, "day_5")); Assert.False(Earned(all, "level_5"));

        var fresh = await kid.GetFromJsonAsync<JsonElement>("/api/me/badges/new");
        Assert.Equal(4, fresh.GetArrayLength());                                                  // first_solve, solver_10, streak_3, streak_7
        Assert.Equal(0, (await kid.GetFromJsonAsync<JsonElement>("/api/me/badges/new")).GetArrayLength());   // announced once

        using (var scope = app.Services.CreateScope())   // the numbers fall (a streak breaks, data is trimmed): what was earned stays
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SolveRecords.Where(r => r.UserId == id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LongestStreak, 0));
        }
        Assert.True(Earned(await All(), "solver_10"));
        Assert.True(Earned(await All(), "streak_7"));

        using (var scope = app.Services.CreateScope())   // a silent run (the startup backfill) earns without a toast
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SolveRecords.Add(new SolveRecord { UserId = id, ProblemKey = "bank:500", Level = ProblemLevel.Easy });
            await db.SaveChangesAsync();
            await db.UserBadges.Where(b => b.UserId == id).ExecuteDeleteAsync();
            var added = await scope.ServiceProvider.GetRequiredService<BadgeService>().EvaluateAsync(id, silent: true);
            Assert.Contains("first_solve", added);
        }
        Assert.Equal(0, (await kid.GetFromJsonAsync<JsonElement>("/api/me/badges/new")).GetArrayLength());
    }
}
