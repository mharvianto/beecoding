using System.Security.Claims;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>Builds the "who am I" payload; shared by password login, Google, passkeys and the second-factor step.</summary>
public class MeDtoBuilder(AppDbContext db, AdminAccess admin, MfaPolicy policy)
{
    public async Task<MeDto> BuildAsync(User user, ClaimsPrincipal? principal = null)
    {
        bool adminHeld = policy.AdminHeld(user.Email, MfaPolicy.HasFactor(user), principal);
        if (admin.IsAdminEmail(user.Email)) admin.SetHeld(user.Email, adminHeld);   // a sign-in builds this before the gate has seen the session
        var isAdmin = admin.HasAdminPowers(user.Email);
        // A platform super admin can manage every organization (see OrgAccess.CanManageAsync),
        // so they should see the Organization nav link too, not just members with OrgRole.Admin.
        var hasOrgAdmin = isAdmin || await db.OrganizationMemberships.AnyAsync(m => m.UserId == user.Id && m.Role == OrgRole.Admin);
        return new MeDto(user.Id, user.Email, user.DisplayName, user.Role.ToString(), isAdmin, hasOrgAdmin,
            user.EmailVerifiedAt != null, user.HasPassword, MfaPolicy.HasFactor(user), await policy.RequiredAsync(user, principal),
            user.MfaOfferHiddenUntil is { } until && until > DateTime.UtcNow,
            await db.OrganizationInvites.CountAsync(i => i.UserId == user.Id), adminHeld);
    }
}
