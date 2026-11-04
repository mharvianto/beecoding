using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class NotificationTests
{
    [Fact]
    public async Task Comments_on_one_card_pile_into_one_row_and_only_a_read_notification_can_be_deleted()
    {
        using var app = new TestApp();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var teacher = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var student = new User { Email = "s@test.dev", DisplayName = "S", Role = UserRole.Student, PasswordHash = "x" };
            db.Users.Add(student);
            var post = new Post { BoardId = (await db.Boards.FirstAsync()).Id, ProblemId = (await db.Problems.FirstAsync()).Id, UserId = teacher.Id };
            db.Posts.Add(post);
            await db.SaveChangesAsync();

            var svc = scope.ServiceProvider.GetRequiredService<NotificationService>();
            for (int i = 1; i <= 3; i++)
            {
                var c = new PostComment { PostId = post.Id, UserId = student.Id, Body = $"comment {i}" };
                db.PostComments.Add(c);
                await db.SaveChangesAsync();
                await svc.CommentAddedAsync(post, c, "S", false);
            }
        }

        var teacherHttp = app.CreateClient();
        (await teacherHttp.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

        var list = await teacherHttp.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/notifications");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal(3, item.GetProperty("count").GetInt32());
        Assert.Equal("comment 3", item.GetProperty("snippet").GetString());
        var id = item.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict, (await teacherHttp.DeleteAsync($"/api/notifications/{id}")).StatusCode);   // unread: stays
        (await teacherHttp.PostAsync($"/api/notifications/{id}/read", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await teacherHttp.DeleteAsync($"/api/notifications/{id}")).StatusCode);
        Assert.Equal(0, (await teacherHttp.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/notifications")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task The_sweep_drops_old_read_ones_keeps_the_rest_and_trims_to_the_newest_200()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var uid = (await db.Users.FirstAsync()).Id;
        var now = DateTime.UtcNow;
        Notification N(DateTime created, DateTime? read) => new() { UserId = uid, Kind = "report", ReportId = 1, ActorName = "x", CreatedAt = created, ReadAt = read };

        var oldRead = N(now.AddDays(-10), now.AddDays(-8));      // read 8 days ago: over the 7-day default, goes
        var recentRead = N(now.AddDays(-10), now.AddDays(-1));   // read yesterday: stays
        var oldUnread = N(now.AddDays(-10), null);               // never read, but younger than 30 days: stays
        db.Notifications.AddRange(oldRead, recentRead, oldUnread);
        for (int i = 0; i < 205; i++) db.Notifications.Add(N(now.AddMinutes(-i), null));   // newer than the three above
        await db.SaveChangesAsync();

        var janitor = new NotificationJanitor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
            app.Services.GetRequiredService<PlatformRuntimeConfig>(), NullLogger<NotificationJanitor>.Instance);
        await janitor.SweepAsync(CancellationToken.None);

        var left = await db.Notifications.AsNoTracking().Where(n => n.UserId == uid).ToListAsync();
        Assert.DoesNotContain(left, n => n.Id == oldRead.Id);
        Assert.Equal(NotificationJanitor.MaxPerUser, left.Count);                  // 208 -> 202 after the read one went -> trimmed to 200
        Assert.DoesNotContain(left, n => n.Id == recentRead.Id || n.Id == oldUnread.Id);   // the two oldest of the rest were trimmed
    }
}
