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

    /// <summary>The policy applies and this (non-LTI) session's account doesn't have a factor yet.</summary>
    public async Task<bool> RequiredAsync(User user, ClaimsPrincipal? principal = null) =>
        !IsLtiSession(principal) && await AppliesToAsync(user.Id, user.Email, user.Role);

    /// <summary>For the gate: applies and the account has no factor yet.</summary>
    public async Task<bool> BlockedAsync(int userId, string? email, ClaimsPrincipal principal)
    {
        if (!AnyRequired || IsLtiSession(principal)) return false;
        var u = await _db.Users.Where(x => x.Id == userId && x.DeletedAt == null)
            .Select(x => new { x.Email, x.Role, x.TotpEnabledAt, x.EmailMfaEnabledAt }).FirstOrDefaultAsync();
        if (u is null || u.TotpEnabledAt != null || u.EmailMfaEnabledAt != null) return false;
        return await AppliesToAsync(userId, u.Email, u.Role);
    }
}

/// <summary>Holds an account that the policy requires to use two-step verification, but hasn't set it up,
/// on <c>/api/auth/*</c> (where it can set it up). Everything else under /api and /hubs answers 403.</summary>
public sealed class MfaRequirementGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, MfaPolicy policy)
    {
        var path = ctx.Request.Path;
        bool guarded = (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
                       && !path.StartsWithSegments("/api/auth");
        if (guarded && policy.AnyRequired && ctx.User.Identity?.IsAuthenticated == true
            && int.TryParse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            && await policy.BlockedAsync(userId, ctx.User.FindFirst(ClaimTypes.Email)?.Value, ctx.User))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { message = "Turn on two-step verification to continue.", code = "mfa_required" });
            return;
        }
        await next(ctx);
    }
}
