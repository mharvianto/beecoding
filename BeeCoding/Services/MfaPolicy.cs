using System.Security.Claims;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// The platform's two-step-verification policy (toggles in Admin &gt; Users): admins, organization admins and
/// teachers can each be required to have an authenticator app or email code turned on. Not required by
/// default. A required account that lacks one is held on its Account page until it sets one up
/// (<see cref="MfaRequirementGate"/>). Sessions started by an LMS launch are exempt — the LMS is the identity provider.
/// </summary>
public class MfaPolicy(AppDbContext db, AdminAccess admin, PlatformRuntimeConfig cfg)
{
    private readonly AppDbContext _db = db;
    private readonly AdminAccess _admin = admin;
    private readonly PlatformRuntimeConfig _cfg = cfg;

    public bool AnyRequired => _cfg.MfaRequireAdmin || _cfg.MfaRequireOrgAdmin || _cfg.MfaRequireTeacher;

    /// <summary>Does the policy apply to this account (regardless of whether it already complies)?</summary>
    public async Task<bool> AppliesToAsync(int userId, string email, UserRole role)
    {
        if (!AnyRequired) return false;
        if (_cfg.MfaRequireAdmin && _admin.IsAdminEmail(email)) return true;
        if (_cfg.MfaRequireTeacher && role == UserRole.Teacher) return true;
        return _cfg.MfaRequireOrgAdmin
            && await _db.OrganizationMemberships.AnyAsync(m => m.UserId == userId && m.Role == OrgRole.Admin);
    }

    public static bool IsLtiSession(ClaimsPrincipal? principal) =>
        principal?.FindFirst(CookieSignIn.ViaClaim)?.Value == "lti";

    public static bool HasFactor(User u) => u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null;

    /// <summary>The rules that lock the WHOLE app until a factor is on: teachers and organization admins. The platform-admin rule
    /// does not: it only withholds the admin rights (<see cref="AdminHeld"/>), so a newly made admin keeps working as before.</summary>
    private async Task<bool> LocksAsync(int userId, UserRole role) =>
        (_cfg.MfaRequireTeacher && role == UserRole.Teacher)
        || (_cfg.MfaRequireOrgAdmin && await _db.OrganizationMemberships.AnyAsync(m => m.UserId == userId && m.Role == OrgRole.Admin));

    /// <summary>A lock rule applies to this (non-LTI) session's account.</summary>
    public async Task<bool> RequiredAsync(User user, ClaimsPrincipal? principal = null) =>
        !IsLtiSession(principal) && await LocksAsync(user.Id, user.Role);

    /// <summary>Is this account's admin power withheld for lack of a factor?</summary>
    public bool AdminHeld(string email, bool hasFactor, ClaimsPrincipal? principal = null) =>
        _cfg.MfaRequireAdmin && !hasFactor && !IsLtiSession(principal) && _admin.IsAdminEmail(email);

    public async Task<bool> AdminHeldAsync(int userId, string email, ClaimsPrincipal principal) =>
        _cfg.MfaRequireAdmin && AdminHeld(email,
            await _db.Users.AnyAsync(u => u.Id == userId && (u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null)), principal);

    /// <summary>For the gate: a lock rule applies and the account has no factor yet.</summary>
    public async Task<bool> BlockedAsync(int userId, ClaimsPrincipal principal)
    {
        if (!AnyRequired || IsLtiSession(principal)) return false;
        var u = await _db.Users.Where(x => x.Id == userId && x.DeletedAt == null)
            .Select(x => new { x.Email, x.Role, x.TotpEnabledAt, x.EmailMfaEnabledAt }).FirstOrDefaultAsync();
        if (u is null || u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null) return false;
        return await LocksAsync(userId, u.Role);
    }
}

/// <summary>Marks an admin without a factor as held (no admin rights), and holds an account that a lock rule requires to use two-step verification, but hasn't set it up,
/// on <c>/api/auth/*</c> (where it can set it up). Everything else under /api and /hubs answers 403.</summary>
public sealed class MfaRequirementGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, MfaPolicy policy, AdminAccess admin)
    {
        var email = ctx.User.FindFirst(ClaimTypes.Email)?.Value;
        if (email is not null && admin.IsAdminEmail(email) && int.TryParse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            admin.SetHeld(email, await policy.AdminHeldAsync(adminId, email, ctx.User));
        var path = ctx.Request.Path;
        bool guarded = (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
                       && !path.StartsWithSegments("/api/auth");
        if (guarded && policy.AnyRequired && ctx.User.Identity?.IsAuthenticated == true
            && int.TryParse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            && await policy.BlockedAsync(userId, ctx.User))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { message = "Turn on two-step verification to continue.", code = "mfa_required" });
            return;
        }
        await next(ctx);
    }
}
