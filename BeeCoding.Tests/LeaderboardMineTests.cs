using System.Net.Http.Json;
using BeeCoding.Controllers;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class LeaderboardMineTests
{
    [Fact]
    public async Task The_page_carries_the_callers_own_row_and_the_page_it_is_on_and_nothing_when_unranked()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.AddRange(Enumerable.Range(1, 25).Select(i => new User { Email = $"u{i}@test.dev", DisplayName = $"U{i}", Role = UserRole.Student, PasswordHash = "x", Xp = 1000 + i }));
            await db.Users.Where(u => u.Email == "kid@test.dev").ExecuteUpdateAsync(s => s.SetProperty(u => u.Xp, 5));
            await db.SaveChangesAsync();
        }

        var first = await kid.GetFromJsonAsync<LeaderboardPageDto>("/api/leaderboard?page=1&pageSize=20");
        Assert.DoesNotContain(first!.Rows, r => r.Me);   // not on page 1 ...
        Assert.NotNull(first.Mine);                       // ... but the response still has the row and the page
        Assert.Equal(26, first.Mine!.Rank);
        Assert.True(first.Mine.Me);
        Assert.Equal(2, first.MyPage);

        var second = await kid.GetFromJsonAsync<LeaderboardPageDto>("/api/leaderboard?page=2&pageSize=20");
        Assert.Contains(second!.Rows, r => r.Me);

        // unranked in a scope: a period with no solves has no row for the caller
        var month = await kid.GetFromJsonAsync<LeaderboardPageDto>("/api/leaderboard?period=1m");
        Assert.Null(month!.Mine);
        Assert.Null(month.MyPage);
    }
}
