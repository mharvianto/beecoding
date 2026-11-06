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
public class NotificationService(AppDbContext db, IBoardNotifier notifier, WebPushService webPush, AdminAccess admin, EmailService email,
    PlatformRuntimeConfig platform, IConfiguration cfg, ILogger<NotificationService> log)
{
    private readonly EmailService _email = email;
    private readonly PlatformRuntimeConfig _platform = platform;
    private readonly IConfiguration _cfg = cfg;
    private readonly AdminAccess _admin = admin;
    private readonly WebPushService _webPush = webPush;
    private readonly AppDbContext _db = db;
    private readonly IBoardNotifier _notifier = notifier;

    public async Task ReactionAddedAsync(Post post, int actorId, string actorName, bool actorIsStaff, string emoji)
    {
        if (actorId == post.UserId) return;
        var (bell, mail) = await ChannelsAsync(post.UserId, "reaction");
        if (!bell) return;

        var n = await UnreadReactionAsync(post);
        var isNew = n is null;
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
        await PushAsync(n, isNew ? mail : null);   // piles into one unread row, so one email per pile
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
        var (bell, mail) = await ChannelsAsync(post.UserId, "comment");
        if (!bell) return;

        // Like reactions: comments on one card pile into a single unread row ("3 new comments") instead of one each.
        var n = await UnreadAsync(post, "comment");
        var isNew = n is null;
        if (n is null)
        {
            n = new Notification { UserId = post.UserId, Kind = "comment", BoardId = post.BoardId, ProblemId = post.ProblemId, PostId = post.Id };
            _db.Notifications.Add(n);
        }
        else
        {
            n.Count++;
            n.CreatedAt = DateTime.UtcNow;
        }
        n.CommentId = comment.Id;   // the latest one: deleting it takes it back out
        n.ActorUserId = comment.UserId; n.ActorName = Trim(actorName, 120); n.ActorIsStaff = actorIsStaff;
        n.Snippet = Trim(comment.Body, 200);
        await _db.SaveChangesAsync();
        await PushAsync(n, isNew ? mail : null);
    }

    public async Task CommentRemovedAsync(int commentId)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.CommentId == commentId);
        if (n is null) return;
        // shortcut: only the latest comment of a group is tracked by id; an earlier one deleted leaves the count one high.
        if (n.Count > 1 && n.ReadAt == null) { n.Count--; await _db.SaveChangesAsync(); await PushAsync(n); }
        else await RemoveAsync(n);
    }

    /// <summary>The student took the problem back: the admins' unread "submitted for review" rows no longer point at anything.</summary>
    public async Task ReviewWithdrawnAsync(int submitterId, string problemTitle)
    {
        try
        {
            var title = Trim(problemTitle, 200);
            var rows = await _db.Notifications.Where(x => x.Kind == "review-submitted" && x.ActorUserId == submitterId
                && x.TargetTitle == title && x.ReadAt == null).ToListAsync();
            foreach (var n in rows) await RemoveAsync(n);
        }
        catch (Exception ex) { log.LogWarning(ex, "Couldn't clear the review notifications of a withdrawn problem"); }
    }

    public async Task<int> UnreadCountAsync(int userId) => await UnreadQuery(userId).CountAsync();

    /// <summary>Rows that still make sense to show: report/proposal notifications always, wall ones while their board and
    /// problem still exist (soft-deleted ones are hidden).</summary>
    public IQueryable<Notification> VisibleQuery(int userId) =>
        _db.Notifications.Where(n => n.UserId == userId
            && (n.ReportId != null || n.ProposalId != null || (_db.Boards.Any(b => b.Id == n.BoardId) && _db.Problems.Any(p => p.Id == n.ProblemId))));

    public IQueryable<Notification> UnreadQuery(int userId) => VisibleQuery(userId).Where(n => n.ReadAt == null);

    /// <summary>The newest notifications for the bell, wall and report kinds together.</summary>
    public async Task<int> TotalAsync(int userId) => await VisibleQuery(userId).CountAsync();

    public async Task<List<NotificationDto>> ListAsync(int userId, int take, int skip = 0)
    {
        var rows = await VisibleQuery(userId).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(skip).Take(take).ToListAsync();
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
            var (bell, mail) = await ChannelsAsync(uid, "report");
            if (!bell) continue;
            var n = new Notification
            {
                UserId = uid, Kind = "report", ReportId = report.Id, ActorUserId = report.UserId, ActorName = Trim(reporterName, 120),
                Emoji = "🚩", TargetTitle = Trim(problemTitle, 200), Link = link,
                Snippet = Trim($"{categoryLabel}: {report.Message}", 200),
            };
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync();
            await PushAsync(n, mail);
        }
    }

    /// <summary>The reviewer resolved or dismissed the report: tell the person who filed it.</summary>
    public async Task ReportReviewedAsync(ProblemReport report, int reviewerId, string reviewerName, string problemTitle, string link)
    {
        if (reviewerId == report.UserId || report.Status == ReportStatus.Open) return;
        var (bell, mail) = await ChannelsAsync(report.UserId, "report-update");
        if (!bell) return;
        var n = new Notification
        {
            UserId = report.UserId, Kind = "report-update", ReportId = report.Id, ActorUserId = reviewerId, ActorName = Trim(reviewerName, 120),
            Emoji = report.Status == ReportStatus.Resolved ? "✅" : "✖️", TargetTitle = Trim(problemTitle, 200), Link = link,
            Snippet = string.IsNullOrWhiteSpace(report.Note) ? null : Trim(report.Note, 200),
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await PushAsync(n, mail);
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
                var (bell, mail) = await ChannelsAsync(adminId, "review-submitted");
                if (!bell) continue;
                // A student who withdraws and sends the same problem again must not leave two rows behind: bump the unread one.
                var title = Trim(problemTitle, 200);
                var n = await _db.Notifications.FirstOrDefaultAsync(x => x.UserId == adminId && x.Kind == "review-submitted"
                    && x.ActorUserId == submitterId && x.TargetTitle == title && x.ReadAt == null);
                if (n is null)
                {
                    n = new Notification
                    {
                        UserId = adminId, Kind = "review-submitted", ProposalId = 0, ActorUserId = submitterId, ActorName = Trim(submitterName, 120),
                        Emoji = "📝", TargetTitle = title, Link = "/admin/review",
                    };
                    _db.Notifications.Add(n);
                }
                else { n.CreatedAt = DateTime.UtcNow; mail = null; }   // a resend bumps the unread row: no second email
                await _db.SaveChangesAsync();
                await PushAsync(n, mail);
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Couldn't notify admins of a problem waiting for review"); }
    }

    // ---- problem proposals / practice review -------------------------------

    /// <summary>A member proposed a problem for a board: tell the board owner.</summary>
    public async Task ProposalFiledAsync(ProblemProposal proposal, int ownerId, string proposerName, string problemTitle, string link)
    {
        if (ownerId == proposal.ProposerId) return;
        var (bell, mail) = await ChannelsAsync(ownerId, "proposal");
        if (!bell) return;
        var n = new Notification
        {
            UserId = ownerId, Kind = "proposal", ProposalId = proposal.Id, ActorUserId = proposal.ProposerId, ActorName = Trim(proposerName, 120),
            Emoji = "💡", TargetTitle = Trim(problemTitle, 200), Link = link,
            Snippet = string.IsNullOrWhiteSpace(proposal.Note) ? null : Trim(proposal.Note, 200),
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await PushAsync(n, mail);
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
        var (bell, mail) = await ChannelsAsync(toUserId, kind);
        if (!bell) return;
        var n = new Notification
        {
            UserId = toUserId, Kind = kind, ProposalId = proposalId, ActorUserId = reviewerId, ActorName = Trim(reviewerName, 120),
            Emoji = approved ? "✅" : "✖️", TargetTitle = Trim(problemTitle, 200), Link = link,
            Snippet = string.IsNullOrWhiteSpace(note) ? null : Trim(note, 200),
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await PushAsync(n, mail);
    }

    private Task<Notification?> UnreadReactionAsync(Post post) => UnreadAsync(post, "reaction");

    private Task<Notification?> UnreadAsync(Post post, string kind) =>
        _db.Notifications.FirstOrDefaultAsync(x =>
            x.UserId == post.UserId && x.PostId == post.Id && x.Kind == kind && x.ReadAt == null);

    private async Task RemoveAsync(Notification n)
    {
        _db.Notifications.Remove(n);
        await _db.SaveChangesAsync();
        await _notifier.NotificationRemovedAsync(n.UserId, n.Id, await UnreadCountAsync(n.UserId));
    }

    /// <summary>What this user gets for a kind: whether it shows in the bell (platform and user both allow it) and, when it
    /// should also be emailed (a kind the admin locked ignores the user's mute), the address (SMTP set up, address verified, platform allows email for the kind, user opted in).</summary>
    private async Task<(bool Bell, string? EmailTo)> ChannelsAsync(int userId, string kind)
    {
        if (_platform.NotifyOffKinds.Contains(kind)) return (false, null);
        var u = await _db.Users.Where(x => x.Id == userId)
            .Select(x => new { x.NotifyOffKinds, x.NotifyEmailKinds, x.Email, x.EmailVerifiedAt }).FirstOrDefaultAsync();
        if (u is null || (!_platform.NotifyLockedKinds.Contains(kind) && NotificationKinds.Parse(u.NotifyOffKinds).Contains(kind))) return (false, null);
        var mail = _email.IsConfigured && u.EmailVerifiedAt != null && _platform.NotifyEmailKinds.Contains(kind)
            && NotificationKinds.Parse(u.NotifyEmailKinds).Contains(kind);
        return (true, mail ? u.Email : null);
    }

    private async Task PushAsync(Notification n, string? emailTo = null)
    {
        var dto = await ToDtoAsync(n);
        if (dto is null) return;
        await _notifier.NotificationAsync(n.UserId, dto, await UnreadCountAsync(n.UserId));
        _webPush.Queue(n.UserId, dto);
        if (emailTo is not null) QueueEmail(emailTo, dto);
    }

    /// <summary>Plain-text mail, sent in the background (SMTP can take seconds and must not hold up the click that caused it;
    /// <see cref="EmailService.SendAsync"/> never throws). Without <c>App:PublicUrl</c> there is no link to put in it, so none is sent.</summary>
    private void QueueEmail(string to, NotificationDto d)
    {
        var baseUrl = _cfg["App:PublicUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl)) return;
        var path = d.Link ?? $"/boards/{Uri.EscapeDataString(d.BoardSlug)}?problem={d.ProblemId}&post={d.PostId}";
        var body = $"{EmailText(d)}\n\nOpen it: {baseUrl}{path}\n\nChoose which notifications you get by email: {baseUrl}/account";
        _ = Task.Run(() => _email.SendAsync(to, "BeeCoding: you have a new notification", body));
    }

    public static string EmailText(NotificationDto d)
    {
        var note = string.IsNullOrWhiteSpace(d.Snippet) ? "" : $": \"{d.Snippet}\"";
        return d.Kind switch
        {
            "report" => $"{d.ActorName} reported your problem \"{d.ProblemTitle}\"{note}",
            "report-update" => $"Your report on \"{d.ProblemTitle}\" was {(d.Emoji == "✅" ? "resolved" : "dismissed")} by {d.ActorName}{note}",
            "review-submitted" => $"{d.ActorName} submitted \"{d.ProblemTitle}\" for review",
            "proposal" => $"{d.ActorName} proposed the problem \"{d.ProblemTitle}\" for your board{note}",
            "proposal-update" => $"Your proposal \"{d.ProblemTitle}\" was {(d.Emoji == "✅" ? "approved" : "declined")} by {d.ActorName}{note}",
            "practice-review" => $"Your practice submission \"{d.ProblemTitle}\" was {(d.Emoji == "✅" ? "approved" : "declined")} by {d.ActorName}{note}",
            "comment" => $"{d.ActorName} commented on your card{note}",
            _ => $"{d.ActorName} reacted {d.Emoji} to your card",
        };
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
