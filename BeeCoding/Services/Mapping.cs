using BeeCoding.Models;

namespace BeeCoding.Services;

public static class Mapping
{
    public static ProblemLevel ParseLevel(string? s) =>
        Enum.TryParse<ProblemLevel>((s ?? "").Trim(), ignoreCase: true, out var l) ? l : ProblemLevel.Medium;

    public static string NormalizeTags(string? tags) =>
        string.Join(',', (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .Take(12));

    public static SubmissionDto ToDto(Submission s, int viewerUserId, bool canSeeCode, string authorName, bool isStaff = false)
    {
        bool mine = s.UserId == viewerUserId;
        return new SubmissionDto(
            s.Id, s.ProblemId, s.UserId, authorName,
            s.Status.ToString(), s.Verdict.ToString(),
            s.RuntimeMs, s.MemoryKb, s.Score,
            s.HiddenByStudent, mine,
            (mine || canSeeCode) ? s.Code : null,
            (mine || canSeeCode) ? s.CompilerOutput : "",
            s.CreatedAt, s.JudgedAt, s.Language ?? "",
            // Staff-only, and *not* granted just by "mine" — a hidden test's expected
            // output must never reach the student whose submission it is either.
            isStaff ? s.FailedTest : null,
            s.XpAwarded);
    }

    public static TestCaseDto ToDto(TestCase t) =>
        new(t.Id, t.Stdin, t.ExpectedStdout, t.IsSample, t.Points, t.Position);

    public static ProblemDto ToOwnerDto(Problem p) => new(
        p.Id, p.Slug, p.BoardId, p.Title, p.StatementMarkdown, p.AllowedLanguages,
        p.TimeLimitMs, p.MemoryLimitKb, p.Position, p.Tags, p.Level.ToString(), p.GeneratedByAi,
        p.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id).Select(ToDto).ToList(), p.BannedHeaders, p.BannedSymbols, p.InputFileName);

    public static StudentProblemDto ToStudentDto(Problem p) => new(
        p.Id, p.Slug, p.BoardId, p.Title, p.StatementMarkdown, p.AllowedLanguages,
        p.TimeLimitMs, p.MemoryLimitKb, p.Position, p.Tags, p.Level.ToString(), p.GeneratedByAi,
        p.TestCases.Where(t => t.IsSample).OrderBy(t => t.Position).ThenBy(t => t.Id).Select(ToDto).ToList(), p.BannedHeaders, p.BannedSymbols, p.InputFileName);

    public static TestCaseDto ToDto(BankTestCase t) =>
        new(t.Id, t.Stdin, t.ExpectedStdout, t.IsSample, t.Points, t.Position);

    public static BankSummaryDto ToSummary(BankProblem b, int viewerUserId) => new(
        b.Id, b.Slug, b.Title, b.AllowedLanguages, b.Level.ToString(), b.Tags, b.IsPublic,
        b.OwnerId == viewerUserId, b.Owner?.DisplayName ?? "teacher",
        b.TestCases.Count, b.TestCases.Count(t => t.IsSample), b.UpdatedAt, b.GeneratedByAi, b.PendingReview);

    /// <summary>Hidden test cases are only exposed to the bank problem's owner.</summary>
    public static BankProblemDto ToDto(BankProblem b, int viewerUserId)
    {
        bool mine = b.OwnerId == viewerUserId;
        var tests = b.TestCases
            .Where(t => mine || t.IsSample)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(ToDto).ToList();
        return new(
            b.Id, b.Slug, b.Title, b.StatementMarkdown, b.AllowedLanguages,
            b.TimeLimitMs, b.MemoryLimitKb, b.Level.ToString(), b.Tags, b.IsPublic, b.GeneratedByAi,
            b.PendingReview, mine, b.Owner?.DisplayName ?? "teacher", b.UpdatedAt, tests, b.BannedHeaders, b.BannedSymbols, b.InputFileName);
    }

    public static ProblemSummaryDto ToSummary(Problem p) =>
        new(p.Id, p.Slug, p.Title, p.Position, p.TimeLimitMs, p.MemoryLimitKb, p.AllowedLanguages, p.Tags, p.Level.ToString());

    public static BankSubmissionDto ToDto(BankSubmission s, bool withCode = true) => new(
        s.Id, s.BankProblemId, s.Status.ToString(), s.Verdict.ToString(),
        s.RuntimeMs, s.MemoryKb, s.Score, s.CompilerOutput,
        s.CreatedAt, s.JudgedAt, withCode ? s.Code : null, s.Language ?? "", s.XpAwarded);

    // Practice statements are always served as an encrypted image, so the statement text
    // and expected outputs are never sent as JSON (see StatementController.PracticeProblem).
    // Sample *inputs* are still returned so the Run box can be pre-filled — they're already
    // fully visible in the rendered statement image anyway.
    public static PracticeProblemDto ToPracticeDto(BankProblem b, bool solved) => new(
        b.Id, b.Slug, b.Title, "", b.AllowedLanguages,
        b.TimeLimitMs, b.MemoryLimitKb, b.Level.ToString(), b.Tags,
        b.TestCases.Where(t => t.IsSample)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => new TestCaseDto(t.Id, t.Stdin, "", true, t.Points, t.Position))
            .ToList(),
        solved, b.BannedHeaders, b.BannedSymbols, b.InputFileName);
}
