using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Tells a board's students when a session (problem group) opens and when it is about to close (within the hour). Checks every
/// couple of minutes; each group is claimed with a conditional UPDATE on <c>OpenNotifiedAt</c>/<c>CloseNotifiedAt</c> first, so
/// several instances never notify twice, and editing a date clears the marker so the new date notifies again. A group that opened
/// more than a day ago is only marked (nothing is announced for something old, e.g. right after this was first deployed).
/// </summary>
public sealed class GroupScheduleNotifier(IServiceScopeFactory scopes, ILogger<GroupScheduleNotifier> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ClosingWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaleOpen = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepAsync(DateTime.UtcNow, stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task SweepAsync(DateTime now, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var opening = await db.ProblemGroups.Include(g => g.Board)
                .Where(g => !g.Hidden && g.OpensAt != null && g.OpensAt <= now && g.OpenNotifiedAt == null).ToListAsync(ct);
            foreach (var g in opening)
            {
                if (!await db.Problems.AnyAsync(p => p.GroupId == g.Id && !p.Hidden, ct)) continue;   // nothing to open yet: look again later
                if (await db.ProblemGroups.Where(x => x.Id == g.Id && x.OpenNotifiedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.OpenNotifiedAt, now), ct) == 0) continue;
                if (g.OpensAt < now - StaleOpen) continue;
                await Announce("group-opens", g, db, notifications, ct);
            }

            var closing = await db.ProblemGroups.Include(g => g.Board)
                .Where(g => !g.Hidden && g.ClosesAt != null && g.ClosesAt > now && g.ClosesAt <= now + ClosingWindow
                    && (g.OpensAt == null || g.OpensAt <= now) && g.CloseNotifiedAt == null).ToListAsync(ct);
            foreach (var g in closing)
            {
                if (await db.ProblemGroups.Where(x => x.Id == g.Id && x.CloseNotifiedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.CloseNotifiedAt, now), ct) == 0) continue;
                await Announce("group-closing", g, db, notifications, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "group schedule sweep failed");
        }
    }

    private static async Task Announce(string kind, ProblemGroup g, AppDbContext db, NotificationService notifications, CancellationToken ct)
    {
        if (g.Board is null) return;
        var students = await db.BoardMemberships.Where(m => m.BoardId == g.BoardId && m.Role == MembershipRole.Student).Select(m => m.UserId).ToListAsync(ct);
        await notifications.GroupScheduleAsync(kind, students, g.Board.Slug, g.Board.Title, g.Title);
    }
}
