using BeeCoding.Models;

namespace BeeCoding.Services;

/// <summary>What a problem must have before it is saved — the same rules for a board problem and a bank problem.
/// Without them an empty title, an empty statement or zero tests (every submission would be Accepted) all saved fine.</summary>
public static class ProblemRules
{
    public const int MaxTitle = 200;      // the column length
    public const int MaxStatement = 50_000;

    /// <param name="tests">The submitted tests; null means "leave the stored ones alone" (only valid when updating).</param>
    /// <returns>The message to show, or null when the problem is fine.</returns>
    public static string? Validate(string? title, string? statement, IEnumerable<UpsertTestCaseDto>? tests, bool testsRequired)
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
        return null;
    }

    /// <summary>Samples are shown to everyone, so a problem made only of them could be "solved" by printing them back.</summary>
    public const string NeedsHiddenTest = "Add at least one hidden (non-sample) test case — samples are public, so a solution could just print them.";
}
