using BeeCoding.Models;
using BeeCoding.Services;

namespace BeeCoding.Services.Judge;

/// <summary>
/// Push notifications to board clients. Implemented over SignalR; kept as an interface
/// so the judge worker does not depend on the hub directly.
/// </summary>
public interface IBoardNotifier
{
    /// <summary>A submission finished judging: tell the board to refresh its grid.</summary>
    Task ProgressChangedAsync(int boardId, int problemId, int authorUserId);

    /// <summary>Send the author their own (unredacted) submission result.</summary>
    Task SubmissionResultAsync(int authorUserId, SubmissionDto submission);

    /// <summary>Problem added / edited / removed.</summary>
    Task ProblemChangedAsync(int boardId);

    /// <summary>Board exam-mode toggled.</summary>
    Task ExamModeChangedAsync(int boardId, bool examMode);

    /// <summary>Board settings changed (exam / protect / lecturing) — clients should re-fetch the board.</summary>
    Task BoardSettingsChangedAsync(int boardId);

    /// <summary>Teacher toggled per-student visibility.</summary>
    Task MemberVisibilityChangedAsync(int boardId, int studentUserId, bool hiddenByTeacher);

    /// <summary>A wall post changed: note edited, reaction toggled, comment added.</summary>
    Task WallChangedAsync(int boardId);

    /// <summary>A practice (bank) submission finished judging.</summary>
    Task PracticeResultAsync(int userId, BankSubmissionDto submission);

    /// <summary>The user's XP total / level changed.</summary>
    Task ProgressBumpedAsync(int userId, ProgressDto progress);

    /// <summary>A submission (board or practice) is mid-grading: test <c>current</c> of
    /// <c>total</c> is about to run.</summary>
    Task SubmissionProgressAsync(int userId, string kind, int submissionId, int current, int total);
}
