namespace BeeCoding.Services;

/// <summary>
/// Minimum viable password strength check: length + a blocklist of the most common/trivial
/// passwords, rather than arbitrary complexity rules (require uppercase/digit/symbol).
/// NIST 800-63B and most current guidance find complexity rules push users toward
/// predictable patterns (e.g. "Password1!") instead of actually stronger passwords — length
/// plus a check against known-common passwords is more effective.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    // Not exhaustive — just the passwords an attacker tries first. Case-insensitive.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passw0rd", "p@ssw0rd", "p@ssword",
        "12345678", "123456789", "1234567890", "123123123", "qwertyui", "qwerty123",
        "letmein1", "letmein12", "welcome1", "welcome123", "admin1234", "admin123",
        "abc123456", "iloveyou1", "monkey123", "dragon123", "11111111", "00000000",
        "1q2w3e4r5t", "1234qwer", "qazwsx123", "zaq12wsx1", "changeme1", "default1",
        "guest1234", "student123", "teacher123", "football1", "baseball1", "superman1",
        "trustno1x", "sunshine1", "whatever1", "computer1", "internet1", "starwars1",
    };

    /// <summary>Null if OK, else a user-facing rejection reason.</summary>
    public static string? Validate(string? password, string? email = null)
    {
        var p = password ?? "";
        if (p.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (Common.Contains(p.Trim())) return "That password is too common — please choose a less predictable one.";
        var localPart = (email ?? "").Split('@')[0];
        if (localPart.Length > 0 && p.Equals(localPart, StringComparison.OrdinalIgnoreCase))
            return "Password can't be the same as your email.";
        return null;
    }
}
