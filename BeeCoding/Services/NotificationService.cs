using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// In-app notifications: someone reacted to or commented on a wall card (to its author), a problem was reported
/// (to its owner / board staff), or a report was resolved or dismissed (to the reporter).
/// Rows are persisted (so the navbar bell survives reloads and offline time) and also pushed
/// live over SignalR. Reactions on the same post coalesce into one unread row; an author never
/// gets notified about their own activity.
/// </summary>
public class NotificationService(AppDbContext db, IBoardNotifier notifier)
{
    private readonly AppDbContext _db = db;
    private readonly IBoardNotifier _notifier = notifier;

    public async Task ReactionAddedAsync(Post post, int actorId, string actorName, bool actorIsStaff, string emoji)
    {
        if (actorId == post.UserId) return;

        var n = await UnreadReactionAsync(post);
        if (n is null)
        {
            n = new Notification { UserId = post.UserId, Kind = "reaction", BoardId = post.BoardId, ProblemId = post.ProblemId, PostId = post.Id };
            _db.Notifications.Add(n);
        }
        else
        {
            n.Count++;
            n.CreatedAt = DateTime.UtcNow;   // bump to the top of the bell
        }
        n.ActorUserId = actorId;
        n.ActorName = Trim(actorName, 120);
        n.ActorIsStaff = actorIsStaff;
        n.Emoji = emoji;
        await _db.SaveChangesAsync();
        await PushAsync(n);
    }

    /// <summary>A reaction was toggled off: take it back out of the still-unread notification.</summary>
    public async Task ReactionRemovedAsync(Post post, int actorId)
    {
        if (actorId == post.UserId) return;
        var n = await UnreadReactionAsync(post);
        if (n is null) return;

        if (n.Count > 1) { n.Count--; await _db.SaveChangesAsync(); await PushAsync(n); }
        else await RemoveAsync(n);
    }

    public async Task CommentAddedAsync(Post post, PostComment comment, string actorName, bool actorIsStaff)
    {
        if (comment.UserId == post.UserId) return;

        var n = new Notification
        {
            UserId = post.UserId, Kind = "comment", BoardId = post.BoardId, ProblemId = post.ProblemId, PostId = post.Id,
            CommentId = comment.Id, ActorUserId = comment.UserId, ActorName = Trim(actorName, 120), ActorIsStaff = actorIsStaff,
            Snippet = Trim(comment.Body, 200),
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await PushAsync(n);
    }

    public async Task CommentRemovedAsync(int commentId)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.CommentId == commentId);
        if (n is not null) await RemoveAsync(n);
    }

    public async Task<int> UnreadCountAsync(int userId) => await UnreadQuery(userId).CountAsync();

    /// <summary>Rows that still make sense to show: report notifications always, wall ones while their board and
    /// problem still exist (soft-deleted ones are hidden).</summary>
    public IQueryable<Notification> VisibleQuery(int userId) =>
        _db.Notifications.Where(n => n.UserId == userId
            && (n.ReportId != null || (_db.Boards.Any(b => b.Id == n.BoardId) && _db.Problems.Any(p => p.Id == n.ProblemId))));

    public IQueryable<Notification> UnreadQuery(int userId) => VisibleQuery(userId).Where(n => n.ReadAt == null);

    /// <summary>The newest notifications for the bell, wall and report kinds together.</summary>
    public async Task<List<NotificationDto>> ListAsync(int userId, int take)
    {
        var rows = await VisibleQuery(userId).OrderByDescending(n => n.CreatedAt).Take(take).ToListAsync();
        var boardIds = rows.Where(n => n.ReportId == null).Select(n => n.BoardId).Distinct().ToList();
        var problemIds = rows.Where(n => n.ReportId == null).Select(n => n.ProblemId).Distinct().ToList();
        var boards = await _db.Boards.Where(b => boardIds.Contains(b.Id)).Select(b => new { b.Id, b.Slug, b.Title }).ToDictionaryAsync(b => b.Id);
        var problems = await _db.Problems.Where(p => problemIds.Contains(p.Id)).Select(p => new { p.Id, p.Title }).ToDictionaryAsync(p => p.Id);
        return rows.Select(n => n.ReportId != null
            ? ToDto(n, "", "", n.TargetTitle ?? "")
            : ToDto(n, boards[n.BoardId].Slug, boards[n.BoardId].Title, problems[n.ProblemId].Title)).ToList();
    }

    // ---- problem reports ----------------------------------------------------

    /// <summary>A new "this problem is wrong" report: tell the people who can act on it (never the reporter).</summary>
    public async Task ReportFiledAsync(ProblemReport report, string reporterName, string problemTitle, string categoryLabel,
        IEnumerable<int> recipientUserIds, string link)
    {
        foreach (var uid in recipientUserIds.Where(u => u != report.UserId).Distinct())
        {
            var n = new Notification
            {
                UserId = uid, Kind = "report", ReportId = report.Id, ActorUserId = report.UserId, ActorName = Trim(reporterName, 120),
                Emoji = "🚩", TargetTitle = Trim(problemTitle, 200), Link = link,
                Snippet = Trim($"{categoryLabel}: {report.Message}", 200),
            };
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync();
            await PushAsync(n);
        }
    }

    /// <summary>The reviewer resolved or dismissed the report: tell the person who filed it.</summary>
    public async Task ReportReviewedAsync(ProblemReport report, int reviewerId, string reviewerName, string problemTitle, string link)
    {
        if (reviewerId == report.UserId || report.Status == ReportStatus.Open) return;
        var n = new Notification
        {
            UserId = report.UserId, Kind = "report-update", ReportId = report.Id, ActorUserId = reviewerId, ActorName = Trim(reviewerName, 120),
            Emoji = report.Status == ReportStatus.Resolved ? "✅" : "✖️", TargetTitle = Trim(problemTitle, 200), Link = link,
            Snippet = string.IsNullOrWhiteSpace(report.Note) ? null : Trim(report.Note, 200),
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await PushAsync(n);
    }

    private Task<Notification?> UnreadReactionAsync(Post post) =>
        _db.Notifications.FirstOrDefaultAsync(x =>
            x.UserId == post.UserId && x.PostId == post.Id && x.Kind == "reaction" && x.ReadAt == null);

    private async Task RemoveAsync(Notification n)
    {
        _db.Notifications.Remove(n);
        await _db.SaveChangesAsync();
        await _notifier.NotificationRemovedAsync(n.UserId, n.Id, await UnreadCountAsync(n.UserId));
    }

    private async Task PushAsync(Notification n)
    {
        var dto = await ToDtoAsync(n);
        if (dto is null) return;
        await _notifier.NotificationAsync(n.UserId, dto, await UnreadCountAsync(n.UserId));
    }

    private async Task<NotificationDto?> ToDtoAsync(Notification n)
    {
        if (n.ReportId != null) return ToDto(n, "", "", n.TargetTitle ?? "");
        var ctx = await (from b in _db.Boards
                         join p in _db.Problems on n.ProblemId equals p.Id
                         where b.Id == n.BoardId
                         select new { b.Slug, BoardTitle = b.Title, ProblemTitle = p.Title }).FirstOrDefaultAsync();
        return ctx is null ? null : ToDto(n, ctx.Slug, ctx.BoardTitle, ctx.ProblemTitle);
    }

    public static NotificationDto ToDto(Notification n, string boardSlug, string boardTitle, string problemTitle) =>
        new(n.Id, n.Kind, boardSlug, boardTitle, n.ProblemId, problemTitle, n.PostId, n.ActorName, n.ActorIsStaff,
            n.Emoji, n.Snippet, n.Count, n.CreatedAt, n.ReadAt != null, n.Link);

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
