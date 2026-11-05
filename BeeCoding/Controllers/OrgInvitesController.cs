using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>The user's side of organizations: answer an org admin's invitation, see which organizations they belong to, and leave one.
/// Membership gives an org admin sight of the member's practice activity, so it only starts when the user accepts (or the address is on the
/// organization's own domain, see OrgAdminController.AddMember).</summary>
[ApiController]
[Authorize]
[Route("api/org-invites")]
public class OrgInvitesController(AppDbContext db, AuditLog audit) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly AuditLog _audit = audit;

    [HttpGet]
    public async Task<List<MyOrgInviteDto>> Mine() =>
        await _db.OrganizationInvites.Where(i => i.UserId == UserId).OrderBy(i => i.CreatedAt)
            .Select(i => new MyOrgInviteDto(i.Id, i.Organization!.Name, i.Role.ToString(),
                _db.Users.Where(u => u.Id == i.InvitedById).Select(u => u.DisplayName).FirstOrDefault() ?? "an organization admin", i.CreatedAt))
            .ToListAsync();

    [HttpPost("{id:int}/accept")]
    public async Task<IActionResult> Accept(int id)
    {
        var invite = await _db.OrganizationInvites.FirstOrDefaultAsync(i => i.Id == id && i.UserId == UserId);
        if (invite is null) return NotFound();
        if (!await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == invite.OrganizationId && m.UserId == UserId))
            _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = invite.OrganizationId, UserId = UserId, Role = invite.Role });
        _db.OrganizationInvites.Remove(invite);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Two clicks at once (or the org added them by domain at the same moment): the other request won, so they are in.
            if (await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == invite.OrganizationId && m.UserId == UserId)) return NoContent();
            throw;
        }
        await _audit.RecordAsync(UserId, ActorEmail, "org-invite-accept", "Organization", invite.OrganizationId, $"as {invite.Role}");
        return NoContent();
    }

    [HttpPost("{id:int}/decline")]
    public async Task<IActionResult> Decline(int id)
    {
        var invite = await _db.OrganizationInvites.FirstOrDefaultAsync(i => i.Id == id && i.UserId == UserId);
        if (invite is null) return NotFound();
        _db.OrganizationInvites.Remove(invite);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("memberships")]
    public async Task<List<MyOrgDto>> Memberships() =>
        await _db.OrganizationMemberships.Where(m => m.UserId == UserId).OrderBy(m => m.Organization!.Name)
            .Select(m => new MyOrgDto(m.OrganizationId, m.Organization!.Name, m.Role.ToString())).ToListAsync();

    /// <summary>Leave an organization. Its last admin cannot (it would be left with nobody to run it). A member who came in through the
    /// organization's LMS is enrolled again the next time they launch from it.</summary>
    [HttpDelete("memberships/{orgId:int}")]
    public async Task<IActionResult> Leave(int orgId)
    {
        var membership = await _db.OrganizationMemberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == UserId);
        if (membership is null) return NotFound();
        if (membership.Role == OrgRole.Admin
            && !await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == orgId && m.Role == OrgRole.Admin && m.UserId != UserId))
            return Conflict("You are the last admin of this organization — make someone else an admin first.");
        _db.OrganizationMemberships.Remove(membership);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "org-leave", "Organization", orgId, "left");
        return NoContent();
    }
}
