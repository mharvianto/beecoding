using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>Builds the "who am I" payload; shared by password login and the second-factor step.</summary>
public static class MeDtoBuilder
{
    public static async Task<MeDto> BuildAsync(AppDbContext db, AdminAccess admin, User user)
    {
        var isAdmin = admin.IsAdminEmail(user.Email);
        // A platform super admin can manage every organization (see OrgAccess.CanManageAsync),
        // so they should see the Organization nav link too, not just members with OrgRole.Admin.
        var hasOrgAdmin = isAdmin || await db.OrganizationMemberships.AnyAsync(m => m.UserId == user.Id && m.Role == OrgRole.Admin);
        return new MeDto(user.Id, user.Email, user.DisplayName, user.Role.ToString(), isAdmin, hasOrgAdmin, user.EmailVerifiedAt != null, user.HasPassword);
    }
}
