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

    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cleaned = Disallowed().Replace(name.Trim(), "");
        return cleaned.Length == 0 ? null : cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}
