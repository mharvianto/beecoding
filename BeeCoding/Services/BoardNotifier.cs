using BeeCoding.Hubs;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.SignalR;

namespace BeeCoding.Services;

public class BoardNotifier(IHubContext<BoardHub> hub, IPresenceTracker presence) : IBoardNotifier
{
    private readonly IHubContext<BoardHub> _hub = hub;
    private readonly IPresenceTracker _presence = presence;

    public Task ProgressChangedAsync(int boardId, int problemId, int authorUserId) =>
        _hub.Clients.Group(BoardHub.BoardGroup(boardId))
            .SendAsync("progressChanged", new { boardId, problemId, userId = authorUserId });

    public Task SubmissionResultAsync(int authorUserId, SubmissionDto submission) =>
        _hub.Clients.User(authorUserId.ToString())
            .SendAsync("submissionResult", submission);

    public Task ProblemChangedAsync(int boardId) =>
        _hub.Clients.Group(BoardHub.BoardGroup(boardId)).SendAsync("problemChanged", new { boardId });

    public Task ExamModeChangedAsync(int boardId, bool examMode) =>
        _hub.Clients.Group(BoardHub.BoardGroup(boardId)).SendAsync("examModeChanged", new { boardId, examMode });

    public Task BoardSettingsChangedAsync(int boardId) =>
        _hub.Clients.Group(BoardHub.BoardGroup(boardId)).SendAsync("boardSettingsChanged", new { boardId });

    public Task MemberVisibilityChangedAsync(int boardId, int studentUserId, bool hiddenByTeacher) =>
        _hub.Clients.Group(BoardHub.BoardGroup(boardId))
            .SendAsync("memberVisibilityChanged", new { boardId, studentUserId, hiddenByTeacher });

    public Task WallChangedAsync(int boardId) =>
        _hub.Clients.Group(BoardHub.BoardGroup(boardId)).SendAsync("wallChanged", new { boardId });

    public Task PracticeResultAsync(int userId, BankSubmissionDto submission) =>
        _hub.Clients.User(userId.ToString()).SendAsync("practiceResult", submission);

    public Task ProgressBumpedAsync(int userId, ProgressDto progress) =>
        _hub.Clients.User(userId.ToString()).SendAsync("progressBumped", progress);

    public Task SubmissionProgressAsync(int userId, string kind, int submissionId, int current, int total) =>
        _hub.Clients.User(userId.ToString())
            .SendAsync("submissionProgress", new { kind, submissionId, current, total });

    public async Task StaffRevokedAsync(int userId, int boardId)
    {
        foreach (var conn in await _presence.ConnectionsOfAsync(boardId, userId))
            await _hub.Groups.RemoveFromGroupAsync(conn, BoardHub.StaffGroup(boardId));
    }

    public async Task MemberRemovedAsync(int userId, int boardId, string boardSlug, string boardTitle, bool left = false)
    {
        // A hub group is joined once, at JoinBoard: take the removed member's open tabs out of the board's groups now, or they would keep
        // receiving the lecture buffer and peers' drafts until they close the tab.
        foreach (var conn in await _presence.ConnectionsOfAsync(boardId, userId))
        {
            await _hub.Groups.RemoveFromGroupAsync(conn, BoardHub.BoardGroup(boardId));
            await _hub.Groups.RemoveFromGroupAsync(conn, BoardHub.StaffGroup(boardId));
            await _presence.RemoveAsync(conn);
        }
        await _hub.Clients.User(userId.ToString()).SendAsync("memberRemoved", new { boardId, boardSlug, boardTitle, left });
    }

    public Task NotificationAsync(int userId, NotificationDto item, int unread) =>
        _hub.Clients.User(userId.ToString()).SendAsync("notification", new { item, unread });

    public Task NotificationRemovedAsync(int userId, int notificationId, int unread) =>
        _hub.Clients.User(userId.ToString()).SendAsync("notificationRemoved", new { id = notificationId, unread });
}
