using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace BeeCoding.Services;

/// <summary>
/// Single source of truth for "is this email an admin" — the union of two sources:
/// <c>Admin:Emails</c> config (comma-separated, always wins, can't be revoked from the UI)
/// and a per-user <c>IsAdmin</c> flag grantable from the admin panel. Config is checked
/// fresh on every call; DB-granted admins are cached in memory (populated at startup, kept
/// in sync by AdminUiController's grant/revoke) so the hot path stays a plain dictionary
/// lookup — no DB round-trip on every request.
///
/// Caveat: the DB-admin cache is per-instance. In a scale-out deployment (multiple app
/// instances), a grant/revoke on one instance isn't visible on the others until they
/// restart — config-based admins are unaffected. Fine for this app's current single-instance
/// deployment; flag if that changes.
/// </summary>
public class AdminAccess(IConfiguration cfg)
{
    private readonly IConfiguration _cfg = cfg;
    private volatile HashSet<string> _dbAdmins = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfiguredAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        (_cfg["Admin:Emails"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>Admin:Emails</c> list, lower-cased.</summary>
    public List<string> ConfiguredEmails() =>
        (_cfg["Admin:Emails"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant()).ToList();

    public bool IsAdminEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && (IsConfiguredAdmin(email) || _dbAdmins.Contains(email));

    /// <summary>Replace the whole DB-granted-admin cache (called once at startup).</summary>
    public void SetDbAdmins(IEnumerable<string> emails) => _dbAdmins = new HashSet<string>(emails, StringComparer.OrdinalIgnoreCase);

    /// <summary>Update the cache after a single grant/revoke — avoids a re-query.</summary>
    public void SetDbAdmin(string email, bool isAdmin)
    {
        var next = new HashSet<string>(_dbAdmins, StringComparer.OrdinalIgnoreCase);
        if (isAdmin) next.Add(email); else next.Remove(email);
        _dbAdmins = next;
    }
}

/// <summary>Marker requirement for the "Admin" authorization policy.</summary>
public class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Evaluates the "Admin" policy against <see cref="AdminAccess"/> instead of a Role claim,
/// so it reflects the current <c>Admin:Emails</c> config rather than what was true at login.
/// </summary>
public class AdminAuthorizationHandler(AdminAccess admin) : AuthorizationHandler<AdminRequirement>
{
    private readonly AdminAccess _admin = admin;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (_admin.IsAdminEmail(context.User.FindFirst(ClaimTypes.Email)?.Value))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
