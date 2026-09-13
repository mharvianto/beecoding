namespace BeeCoding.Services;

/// <summary>
/// Origins of every enabled LTI platform, for the response CSP's frame-ancestors directive.
/// An LTI tool MUST be embeddable in an iframe by its registered platform(s) — both a
/// normal resource-link launch (the LMS embeds the board/problem page inline) and the Deep
/// Linking "Select content" dialog rely on this — so a blanket frame-ancestors 'none' (the
/// right default for an app with no LTI platforms registered) breaks LTI entirely once one
/// is. Warmed at startup and refreshed on every LTI platform write (see
/// AdminUiController/OrgAdminController's Create/Update/Delete).
/// </summary>
public class LtiPlatformOriginsCache
{
    private volatile string[] _origins = Array.Empty<string>();

    /// <summary>Empty = no LTI platforms registered (or none parsed to a valid origin) —
    /// callers should fall back to frame-ancestors 'none'.</summary>
    public IReadOnlyList<string> Origins => _origins;

    public void Set(IEnumerable<string?> issuers)
    {
        _origins = issuers
            .Select(i => string.IsNullOrWhiteSpace(i) ? null : TryOrigin(i))
            .Where(o => o is not null)
            .Cast<string>()
            .Distinct()
            .ToArray();
    }

    private static string? TryOrigin(string issuer) =>
        Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
}
