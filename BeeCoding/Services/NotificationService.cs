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
public class NotificationService(AppDbContext db, IBoardNotifier notifier, WebPushService webPush, AdminAccess admin, ILogger<NotificationService> log)
{
    private readonly AdminAccess _admin = admin;
    private readonly WebPushService _webPush = webPush;
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

    /// <summary>Rows that still make sense to show: report/proposal notifications always, wall ones while their board and
    /// problem still exist (soft-deleted ones are hidden).</summary>
    public IQueryable<Notification> VisibleQuery(int userId) =>
        _db.Notifications.Where(n => n.UserId == userId
            && (n.ReportId != null || n.ProposalId != null || (_db.Boards.Any(b => b.Id == n.BoardId) && _db.Problems.Any(p => p.Id == n.ProblemId))));

    public IQueryable<Notification> UnreadQuery(int userId) => VisibleQuery(userId).Where(n => n.ReadAt == null);

    /// <summary>The newest notifications for the bell, wall and report kinds together.</summary>
    public async Task<List<NotificationDto>> ListAsync(int userId, int take)
    {
        var rows = await VisibleQuery(userId).OrderByDescending(n => n.CreatedAt).Take(take).ToListAsync();
        var boardIds = rows.Where(n => n.ReportId == null && n.ProposalId == null).Select(n => n.BoardId).Distinct().ToList();
        var problemIds = rows.Where(n => n.ReportId == null && n.ProposalId == null).Select(n => n.ProblemId).Distinct().ToList();
        var boards = await _db.Boards.Where(b => boardIds.Contains(b.Id)).Select(b => new { b.Id, b.Slug, b.Title }).ToDictionaryAsync(b => b.Id);
        var problems = await _db.Problems.Where(p => problemIds.Contains(p.Id)).Select(p => new { p.Id, p.Title }).ToDictionaryAsync(p => p.Id);
        return rows.Select(n => n.ReportId != null || n.ProposalId != null
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

    // ---- admins ---------------------------------------------------------------

    /// <summary>Ids of every platform admin (config <c>Admin:Emails</c> plus the ones granted in the panel).</summary>
    public async Task<List<int>> AdminUserIdsAsync()
    {
        var emails = _admin.AllAdminEmails();
        return await _db.Users.Where(u => emails.Contains(u.Email)).Select(u => u.Id).ToListAsync();
    }

    /// <summary>A problem just entered the admin review queue (student or teacher submitted it for Practice): tell the admins.
    /// Never fails the submit — a notification problem is only logged.</summary>
    public async Task ReviewSubmittedAsync(int submitterId, string submitterName, string problemTitle)
    {
        try
        {
            foreach (var adminId in (await AdminUserIdsAsync()).Where(a => a != submitterId))
            {
                var n = new Notification
                {
                    UserId = adminId, Kind = "review-submitted", ProposalId = 0, ActorUserId = submitterId, ActorName = Trim(submitterName, 120),
                    Emoji = "📝", TargetTitle = Trim(problemTitle, 200), Link = "/admin/review",
                };
                _db.Notifications.Add(n);
                await _db.SaveChangesAsync();
                await PushAsync(n);
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Couldn't notify admins of a problem waiting for review"); }
    }

    // ---- problem proposals / practice review -------------------------------

    /// <summary>A member proposed a problem for a board: tell the board owner.</summary>
    public async Task ProposalFiledAsync(ProblemProposal proposal, int ownerId, string proposerName, string problemTitle, string link)
    {
        if (ownerId == proposal.ProposerId) return;
        var n = new Notification
        {
            UserId = ownerId, Kind = "proposal", ProposalId = proposal.Id, ActorUserId = proposal.ProposerId, ActorName = Trim(proposerName, 120),
            Emoji = "💡", TargetTitle = Trim(problemTitle, 200), Link = link,
            Snippet = string.IsNullOrWhiteSpace(proposal.Note) ? null : Trim(proposal.Note, 200),
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await PushAsync(n);
    }

    /// <summary>The board owner approved or declined a proposal: tell the proposer.</summary>
    public async Task ProposalReviewedAsync(ProblemProposal proposal, int reviewerId, string reviewerName, string problemTitle, string link) =>
        await ReviewedAsync("proposal-update", proposal.Id, proposal.ProposerId, reviewerId, reviewerName, problemTitle, link,
            proposal.Status == ProposalStatus.Approved, proposal.ReviewNote);

    /// <summary>An admin approved or rejected a practice submission: tell its owner. Not tied to a
    /// proposal row, so ProposalId is 0 (non-null is what makes the bell show a board-less row).</summary>
    public async Task PracticeReviewedAsync(int ownerId, int reviewerId, string reviewerName, string problemTitle, bool approved, string? note) =>
        await ReviewedAsync("practice-review", 0, ownerId, reviewerId, reviewerName, problemTitle, "/bank", approved, note);

    private async Task ReviewedAsync(string kind, int proposalId, int toUserId, int reviewerId, string reviewerName,
        string problemTitle, string link, bool approved, string? note)
    {
        if (toUserId == reviewerId) return;
        var n = new Notification
        {
            UserId = toUserId, Kind = kind, ProposalId = proposalId, ActorUserId = reviewerId, ActorName = Trim(reviewerName, 120),
            Emoji = approved ? "✅" : "✖️", TargetTitle = Trim(problemTitle, 200), Link = link,
            Snippet = string.IsNullOrWhiteSpace(note) ? null : Trim(note, 200),
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
        _webPush.Queue(n.UserId, dto);
    }

    private async Task<NotificationDto?> ToDtoAsync(Notification n)
    {
        if (n.ReportId != null || n.ProposalId != null) return ToDto(n, "", "", n.TargetTitle ?? "");
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
