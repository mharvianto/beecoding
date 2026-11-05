using System.Text.RegularExpressions;

namespace BeeCoding.Services.Judge;

/// <summary>
/// Optional file-based input: when a problem sets this, a testcase's Stdin content is
/// written to this filename in the sandbox's work directory instead of being piped to the
/// program's stdin — for problems that require fopen()-style file I/O. Null/empty means
/// stdin (the default). No path separators are ever allowed, so the file can only ever
/// land inside the sandboxed work directory.
/// </summary>
public static partial class InputFilePolicy
{
    [GeneratedRegex(@"[^A-Za-z0-9_.-]")]
    private static partial Regex Disallowed();

    // Files the judge itself keeps in the work directory: the compiled program, its source, and the stats file.
    private static readonly string[] Reserved = ["prog", "main.c", "main.cpp"];

    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cleaned = Disallowed().Replace(name.Trim(), "");
        if (cleaned.Length > 64) cleaned = cleaned[..64];
        // "." and ".." survive the character filter but name directories, and writing the test input over the program or the stats
        // file would break every run of the problem: treat those as "no file name" (plain stdin) like any other unusable name.
        if (cleaned.Trim('.').Length == 0 || cleaned.StartsWith("stat_", StringComparison.Ordinal)
            || Reserved.Contains(cleaned, StringComparer.Ordinal)) return null;
        return cleaned;
    }
}
