using System.Globalization;
using System.Text;

namespace BeeCoding.Services;

/// <summary>One rule for names people see on boards and leaderboards: 2–40 characters, no control or invisible characters.</summary>
public static class DisplayNamePolicy
{
    public const int Min = 2, Max = 40;

    /// <summary>Drops control/format characters (newlines, zero-width, bidi overrides), collapses runs of spaces and trims.</summary>
    public static string Clean(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name ?? "")
        {
            if (char.IsWhiteSpace(ch)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }   // tabs/newlines become a space
            else if (char.GetUnicodeCategory(ch) is not (UnicodeCategory.Control or UnicodeCategory.Format)) sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>For people typing their own name: null if OK (<paramref name="clean"/> is what to store), else the reason to show.</summary>
    public static string? Validate(string? name, out string clean)
    {
        clean = Clean(name);
        if (clean.Length < Min) return $"Display name must be at least {Min} characters.";
        if (clean.Length > Max) return $"Display name must be {Max} characters or fewer.";
        return null;
    }

    /// <summary>For names that come from outside (Google, LMS): never refuse a sign-in, clean and cut instead. Falls back when nothing usable is left.</summary>
    public static string FromOutside(string? name, string fallback)
    {
        var clean = Clean(name);
        if (clean.Length > Max) clean = clean[..(char.IsHighSurrogate(clean[Max - 1]) ? Max - 1 : Max)].TrimEnd();
        return clean.Length < Min ? fallback : clean;
    }
}
