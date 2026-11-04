using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using Xunit;

namespace BeeCoding.Tests;

public class VisibilityTests
{
    private static readonly VisibilityService Vis = new();
    private static BoardMembership Author(bool hiddenByTeacher = false) => new() { UserId = 2, HiddenByTeacher = hiddenByTeacher };

    [Fact] public void Staff_sees_everything_even_in_exam_mode() =>
        Assert.True(Vis.CanSeePeerSubmission(1, true, true, Author(true), new Submission { HiddenByStudent = true }));

    [Fact] public void Student_sees_own_work_in_exam_mode() =>
        Assert.True(Vis.CanSeePeerSubmission(2, false, true, Author(true), new Submission { HiddenByStudent = true }));

    [Fact] public void Peer_work_is_blocked_by_each_layer()
    {
        Assert.False(Vis.CanSeePeerSubmission(1, false, true, Author(), new Submission()));                           // exam
        Assert.False(Vis.CanSeePeerSubmission(1, false, false, Author(true), new Submission()));                      // teacher-hidden
        Assert.False(Vis.CanSeePeerSubmission(1, false, false, Author(), new Submission { HiddenByStudent = true })); // student-hidden
        Assert.True(Vis.CanSeePeerSubmission(1, false, false, Author(), new Submission()));
    }

    [Fact] public void Peer_row_is_hidden_only_in_exam_mode()
    {
        Assert.False(Vis.CanSeePeerRow(1, false, true, Author()));
        Assert.True(Vis.CanSeePeerRow(1, false, false, Author()));
    }
}

public class GroupAccessTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static Problem In(ProblemGroup g, bool hidden = false) => new() { Hidden = hidden, Group = g, GroupId = g.Id == 0 ? 1 : g.Id };

    [Fact] public void Hidden_problem_or_group_is_not_student_visible()
    {
        Assert.False(GroupAccess.StudentVisible(In(new ProblemGroup(), hidden: true), Now));
        Assert.False(GroupAccess.StudentVisible(In(new ProblemGroup { Hidden = true }), Now));
    }

    [Fact] public void Group_not_yet_open_is_invisible_and_closed_group_refuses_submits()
    {
        Assert.False(GroupAccess.StudentVisible(In(new ProblemGroup { OpensAt = Now.AddHours(1) }), Now));
        Assert.True(GroupAccess.StudentVisible(In(new ProblemGroup { ClosesAt = Now.AddHours(-1) }), Now));
        Assert.True(GroupAccess.IsClosed(In(new ProblemGroup { ClosesAt = Now.AddHours(-1) }), Now));
        Assert.False(GroupAccess.IsClosed(In(new ProblemGroup { ClosesAt = Now.AddHours(1) }), Now));
    }

    [Fact] public void Exam_mode_comes_from_the_group_else_the_board()
    {
        var board = new Board { ExamMode = true };
        Assert.False(GroupAccess.ExamMode(board, In(new ProblemGroup { ExamMode = false })));
        Assert.True(GroupAccess.ExamMode(board, new Problem()));
    }
}

public class AiUrlTests
{
    [Theory]
    [InlineData("https://api.example.com/v1/chat/completions", true)]
    [InlineData("", true)]
    [InlineData("http://api.example.com/v1", false)]
    [InlineData("https://localhost/v1", false)]
    [InlineData("https://127.0.0.1/v1", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    [InlineData("https://10.1.2.3/v1", false)]
    [InlineData("https://[::1]/v1", false)]
    [InlineData("https://intranet/v1", false)]
    [InlineData("not a url", false)]
    public void Org_admin_urls(string url, bool ok) => Assert.Equal(ok, AiUrl.Validate(url, strict: true) is null);

    [Fact] public void Platform_admin_may_use_a_local_model_server() =>
        Assert.Null(AiUrl.Validate("http://localhost:11434/v1", strict: false));
}
