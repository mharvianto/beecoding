using BeeCoding.Models;

namespace BeeCoding.Services;

/// <summary>What a problem must have before it is saved — the same rules for a board problem and a bank problem.
/// Without them an empty title, an empty statement or zero tests (every submission would be Accepted) all saved fine.</summary>
public static class ProblemRules
{
    public const int MaxTitle = 200;      // the column length
    public const int MaxStatement = 50_000;

    // Characters of input or expected output. A hidden test can legitimately be megabytes for a teacher; a student's problem never needs
    // that, and sign-up is open, so without a cap one account could store gigabytes (50 problems x 30 tests x the 32 MB request limit).
    public const int TeacherMaxTestChars = 5_000_000;
    public const int TeacherMaxTotalChars = 30_000_000;
    public const int StudentMaxTestChars = 100_000;
    public const int StudentMaxTotalChars = 1_000_000;

    /// <param name="tests">The submitted tests; null means "leave the stored ones alone" (only valid when updating).</param>
    /// <returns>The message to show, or null when the problem is fine.</returns>
    public static string? Validate(string? title, string? statement, IEnumerable<UpsertTestCaseDto>? tests, bool testsRequired, bool student = false)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Give the problem a title.";
        if (title.Trim().Length > MaxTitle) return $"The title can be at most {MaxTitle} characters.";
        if (string.IsNullOrWhiteSpace(statement)) return "Write the problem statement.";
        if (statement.Length > MaxStatement) return $"The statement is too long (at most {MaxStatement:N0} characters).";
        if (tests is null) return testsRequired ? "Add at least one test case." : null;

        var list = tests.ToList();
        if (list.Count == 0) return "Add at least one test case.";
        // An untouched "add test" row: nothing in, nothing expected. A real test has at least one of the two.
        if (list.Any(t => string.IsNullOrWhiteSpace(t.Stdin) && string.IsNullOrWhiteSpace(t.ExpectedStdout)))
            return "A test case is empty — fill in its input and expected output, or remove it.";

        var (maxTest, maxTotal) = student ? (StudentMaxTestChars, StudentMaxTotalChars) : (TeacherMaxTestChars, TeacherMaxTotalChars);
        long total = 0;
        foreach (var t in list)
        {
            var size = (t.Stdin?.Length ?? 0) + (t.ExpectedStdout?.Length ?? 0);
            if (size > maxTest) return $"A test case is too large (input plus expected output: at most {maxTest / 1000:N0} KB).";
            total += size;
        }
        if (total > maxTotal) return $"The test cases are too large together (at most {maxTotal / 1_000_000:N0} MB in all).";
        return null;
    }

    /// <summary>Samples are shown to everyone, so a problem made only of them could be "solved" by printing them back.</summary>
    public const string NeedsHiddenTest = "Add at least one hidden (non-sample) test case — samples are public, so a solution could just print them.";
}
