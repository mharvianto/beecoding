using BeeCoding.Models;

namespace BeeCoding.Services;

/// <summary>
/// Single source of truth for who may see whose answers on a board.
/// Used by both the REST progress endpoint and the SignalR payload builder.
///
/// Layers (a student sees a peer's FULL cell only if none of these block it):
///   - exam mode              -> teacher-controlled per problem group (or the board, for ungrouped problems); callers pass the effective flag via GroupAccess.ExamMode
///   - membership.HiddenByTeacher -> teacher-controlled, per student (feature 5, per student)
///   - submission.HiddenByStudent -> student-controlled, per submission (feature 4)
/// Staff (owner/teacher) always see everything. A student always sees their own work.
/// </summary>
public class VisibilityService
{
    public bool IsStaff(MembershipRole role) => role is MembershipRole.Owner or MembershipRole.Teacher;

    /// <summary>Can the viewer see the peer's submission contents / verdict / score?</summary>
    public bool CanSeePeerSubmission(int viewerUserId, bool viewerIsStaff, bool examMode,
        BoardMembership authorMembership, Submission submission)
    {
        if (viewerIsStaff) return true;
        if (authorMembership.UserId == viewerUserId) return true;
        if (examMode) return false;
        if (authorMembership.HiddenByTeacher) return false;
        if (submission.HiddenByStudent) return false;
        return true;
    }

    /// <summary>
    /// Can the viewer see that the peer has a cell on the board at all?
    /// In exam mode a student sees nothing about peers; otherwise a hidden cell
    /// still surfaces as a neutral "attempted" dot (Redacted=true).
    /// </summary>
    public bool CanSeePeerRow(int viewerUserId, bool viewerIsStaff, bool examMode, BoardMembership authorMembership)
    {
        if (viewerIsStaff) return true;
        if (authorMembership.UserId == viewerUserId) return true;
        return !examMode;
    }
}
