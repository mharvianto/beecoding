using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The notification kinds added on top of the wall/report ones: session schedule, new problems, LMS sync failures, org invites, security.</summary>
[Collection("app")]
public class NotificationKindsTests
{
    private static async Task<(User student, Board board, Problem problem)> SeedAsync(AppDbContext db)
    {
        var student = new User { Email = "kind-s@test.dev", DisplayName = "Kind S", Role = UserRole.Student, PasswordHash = "x" };
        db.Users.Add(student);
        var board = await db.Boards.FirstAsync();
        await db.SaveChangesAsync();
        db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = student.Id, Role = MembershipRole.Student });
        await db.SaveChangesAsync();
        return (student, board, await db.Problems.FirstAsync(p => p.BoardId == board.Id));
    }

    [Fact]
    public async Task A_session_that_opens_or_is_about_to_close_notifies_its_students_once_and_an_old_one_is_only_marked()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (student, board, problem) = await SeedAsync(db);
        var now = DateTime.UtcNow;
        var open = new ProblemGroup { BoardId = board.Id, Title = "Session 1", OpensAt = now.AddMinutes(-5), ClosesAt = now.AddMinutes(30) };
        var old = new ProblemGroup { BoardId = board.Id, Title = "Last month", OpensAt = now.AddDays(-3) };
        db.ProblemGroups.AddRange(open, old);
        await db.SaveChangesAsync();
        problem.GroupId = open.Id;
        await db.SaveChangesAsync();
        var inOld = new Problem { BoardId = board.Id, GroupId = old.Id, Title = "Old one", Slug = "old-one-x", StatementMarkdown = "x" };
        db.Problems.Add(inOld);
        await db.SaveChangesAsync();

        var sweeper = new GroupScheduleNotifier(app.Services.GetRequiredService<IServiceScopeFactory>(), new Microsoft.Extensions.Logging.Abstractions.NullLogger<GroupScheduleNotifier>());
        await sweeper.SweepAsync(now, default);
        await sweeper.SweepAsync(now, default);   // a second pass must not announce again

        var rows = await db.Notifications.Where(n => n.UserId == student.Id).ToListAsync();
        Assert.Equal(1, rows.Count(n => n.Kind == "group-opens" && n.TargetTitle == "Session 1"));
        Assert.Equal(1, rows.Count(n => n.Kind == "group-closing" && n.TargetTitle == "Session 1"));
        Assert.DoesNotContain(rows, n => n.TargetTitle == "Last month");
        Assert.NotNull((await db.ProblemGroups.AsNoTracking().FirstAsync(g => g.Id == old.Id)).OpenNotifiedAt);   // marked, so it never comes up again
    }

    [Fact]
    public async Task A_new_problem_piles_into_one_row_per_student_and_a_hidden_one_notifies_nobody()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var (student, board, problem) = await SeedAsync(db);

        problem.Hidden = true;
        await svc.ProblemPublishedAsync(problem);
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.UserId == student.Id));

        problem.Hidden = false;
        await svc.ProblemPublishedAsync(problem);
        await svc.ProblemPublishedAsync(problem);
        var row = await db.Notifications.SingleAsync(n => n.UserId == student.Id && n.Kind == "problem-new");
        Assert.Equal(2, row.Count);
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.UserId != student.Id && n.Kind == "problem-new"));   // staff are not told about their own work
    }

    [Fact]
    public async Task An_LMS_sync_failure_tells_the_staff_once_per_unread_pile_and_security_and_invites_reach_the_user()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var (student, board, _) = await SeedAsync(db);
        var teacher = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
        db.BoardMemberships.RemoveRange(db.BoardMemberships.Where(m => m.BoardId == board.Id && m.UserId == teacher.Id));
        db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = teacher.Id, Role = MembershipRole.Owner });
        await db.SaveChangesAsync();

        await svc.LtiSyncFailedAsync(board.Id);
        await svc.LtiSyncFailedAsync(board.Id);
        await svc.LtiSyncFailedAsync(board.Id);
        var sync = await db.Notifications.SingleAsync(n => n.Kind == "lti-sync-failed" && n.UserId == teacher.Id);
        Assert.Equal(3, sync.Count);
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.Kind == "lti-sync-failed" && n.UserId == student.Id));

        await svc.SecurityAsync(student.Id, "your password was changed");
        await svc.OrgInvitedAsync(student.Id, "Some Org", "Admin A");
        var kinds = await db.Notifications.Where(n => n.UserId == student.Id).Select(n => n.Kind).ToListAsync();
        Assert.Contains("security", kinds);
        Assert.Contains("org-invite", kinds);

        // a platform-wide switch-off silences a new kind like any other
        scope.ServiceProvider.GetRequiredService<PlatformRuntimeConfig>().SetNotifyKinds("security", null, null);
        await svc.SecurityAsync(student.Id, "again");
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == student.Id && n.Kind == "security"));
    }
}
