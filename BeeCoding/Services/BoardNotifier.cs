using BeeCoding.Hubs;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.SignalR;

namespace BeeCoding.Services;

public class BoardNotifier(IHubContext<BoardHub> hub) : IBoardNotifier
{
    private readonly IHubContext<BoardHub> _hub = hub;

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
}
