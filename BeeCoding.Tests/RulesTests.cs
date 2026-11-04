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

public class LoginThrottleTests
{
    [Fact] public void Guessing_from_one_ip_blocks_that_ip_only_for_the_account()
    {
        var t = new LoginThrottle();
        for (int i = 0; i < 10; i++) t.RecordFailure("1.1.1.1", "teacher@x");
        Assert.True(t.IsBlocked("1.1.1.1", "teacher@x"));
        Assert.False(t.IsBlocked("2.2.2.2", "teacher@x"));   // the real teacher elsewhere is not locked out
    }

    [Fact] public void Guessing_spread_over_many_ips_still_blocks_the_account()
    {
        var t = new LoginThrottle();
        for (int i = 0; i < 100; i++) t.RecordFailure($"9.9.9.{i}", "teacher@x");
        Assert.True(t.IsBlocked("2.2.2.2", "teacher@x"));
    }

    [Fact] public void Success_clears_the_counters()
    {
        var t = new LoginThrottle();
        for (int i = 0; i < 10; i++) t.RecordFailure("1.1.1.1", "a@x");
        t.RecordSuccess("1.1.1.1", "a@x");
        Assert.False(t.IsBlocked("1.1.1.1", "a@x"));
    }
}

public class LoginThrottleStrictTests
{
    [Fact] public void Strict_mode_blocks_code_guessing_after_10_tries_even_from_rotating_ips()
    {
        var t = new LoginThrottle();
        for (int i = 0; i < 10; i++) t.RecordFailure($"9.9.9.{i}", "mfa:7");
        Assert.True(t.IsBlocked("2.2.2.2", "mfa:7", strict: true));
        Assert.False(t.IsBlocked("2.2.2.2", "mfa:7"));   // passwords keep the looser total
    }
}

public class AiEffectiveUrlTests
{
    private static string Url(AiProviderRuntime.Config org)
    {
        var rt = new AiProviderRuntime();
        rt.SetOrg(1, org.ApiKey, org.BaseUrl, org.Model, org.GenerateModel);
        var svc = new AiTutorService(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new AiOptions { BaseUrl = "https://platform.example/v1" }),
            new AiRuntimeSettings(), rt, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiTutorService>.Instance);
        svc.UseOrganization(1);
        var tuple = typeof(AiTutorService).GetMethod("Effective", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(svc, null)!;
        return (string)tuple.GetType().GetField("Item2")!.GetValue(tuple)!;   // (ApiKey, BaseUrl, Model, GenerateModel)
    }

    [Fact] public void A_saved_internal_org_url_is_ignored() => Assert.Equal("https://platform.example/v1", Url(new("k", "https://10.0.0.5/v1", null, null)));
    [Fact] public void A_public_org_url_with_its_own_key_is_used() => Assert.Equal("https://api.acme.com/v1", Url(new("k", "https://api.acme.com/v1", null, null)));
    [Fact] public void An_org_url_without_its_own_key_is_ignored() => Assert.Equal("https://platform.example/v1", Url(new(null, "https://api.acme.com/v1", null, null)));
}

public class ProblemRulesTests
{
    private static UpsertTestCaseDto T(string i, string o, bool sample = false) => new(null, i, o, sample, 1, 0);
    private static string? V(string? title, string? statement, IEnumerable<UpsertTestCaseDto>? tests, bool required = true) =>
        ProblemRules.Validate(title, statement, tests, required);

    [Fact] public void A_complete_problem_passes() => Assert.Null(V("A+B", "Add two numbers.", [T("1 2", "3")]));

    [Theory]
    [InlineData("", "text")]
    [InlineData("   ", "text")]
    [InlineData("title", "")]
    [InlineData("title", "  \n ")]
    public void Title_and_statement_are_required(string title, string statement) =>
        Assert.NotNull(V(title, statement, [T("1", "1")]));

    [Fact] public void Zero_tests_are_refused_on_create_but_unsent_tests_are_fine_on_update()
    {
        Assert.NotNull(V("t", "s", [], required: true));
        Assert.NotNull(V("t", "s", null, required: true));
        Assert.Null(V("t", "s", null, required: false));    // update that does not touch the tests
        Assert.NotNull(V("t", "s", [], required: false));   // ...but an update that deletes them all is refused
    }

    [Fact] public void An_untouched_blank_test_row_is_refused_but_an_empty_expected_output_alone_is_not()
    {
        Assert.NotNull(V("t", "s", [T("1", "1"), T("", "")]));
        Assert.Null(V("t", "s", [T("1", "")]));   // "must print nothing" is a legitimate test
    }

    [Fact] public void Overlong_title_and_statement_are_refused()
    {
        Assert.NotNull(V(new string('x', 201), "s", [T("1", "1")]));
        Assert.NotNull(V("t", new string('x', 50_001), [T("1", "1")]));
    }
}
