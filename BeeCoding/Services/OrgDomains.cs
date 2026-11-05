using System.Text.RegularExpressions;

namespace BeeCoding.Services;

/// <summary>The email domains an organization auto-accepts (<see cref="Models.Organization.EmailDomains"/>).</summary>
public static class OrgDomains
{
    private static readonly Regex Valid = new(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", RegexOptions.Compiled);

    /// <summary>Cleans a typed list ("@Binus.ac.id; ui.ac.id"). Returns the stored form, or an error message.</summary>
    public static (string? Value, string? Error) Normalize(string? input)
    {
        var parts = (input ?? "").Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant()).Distinct().ToList();
        foreach (var d in parts)
            if (!Valid.IsMatch(d)) return (null, $"'{d}' is not a domain name (like university.ac.id).");
        var joined = string.Join(", ", parts);
        return joined.Length > 400 ? (null, "That list is too long.") : (joined, null);
    }

    /// <summary>True when the address is on one of the domains or a subdomain of one (mail.univ.ac.id for univ.ac.id).</summary>
    public static bool Matches(string? domains, string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0 || string.IsNullOrWhiteSpace(domains)) return false;
        var host = email[(at + 1)..].ToLowerInvariant();
        return domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }
}
