using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// In-app notifications for wall-post authors: someone reacted to or commented on their card.
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

    /// <summary>Unread rows whose board and problem still exist (soft-deleted ones are hidden).</summary>
    public IQueryable<Notification> UnreadQuery(int userId) =>
        from n in _db.Notifications
        where n.UserId == userId && n.ReadAt == null
        join b in _db.Boards on n.BoardId equals b.Id
        join p in _db.Problems on n.ProblemId equals p.Id
        select n;

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
        var ctx = await (from b in _db.Boards
                         join p in _db.Problems on n.ProblemId equals p.Id
                         where b.Id == n.BoardId
                         select new { b.Slug, BoardTitle = b.Title, ProblemTitle = p.Title }).FirstOrDefaultAsync();
        return ctx is null ? null : ToDto(n, ctx.Slug, ctx.BoardTitle, ctx.ProblemTitle);
    }

    public static NotificationDto ToDto(Notification n, string boardSlug, string boardTitle, string problemTitle) =>
        new(n.Id, n.Kind, boardSlug, boardTitle, n.ProblemId, problemTitle, n.PostId, n.ActorName, n.ActorIsStaff,
            n.Emoji, n.Snippet, n.Count, n.CreatedAt, n.ReadAt != null);

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
