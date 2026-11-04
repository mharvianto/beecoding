using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lsp;
using BeeCoding.Services.Realtime;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;


namespace BeeCoding.Controllers;

/// <summary>Admin panel: organizations and the LTI platform registry. Same route prefix as <see cref="AdminUiController"/>.</summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui")]
public class AdminOrganizationsController(AppDbContext db, AuditLog audit, LtiPlatformOriginsCache ltiOrigins) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly AuditLog _audit = audit;
    private readonly LtiPlatformOriginsCache _ltiOrigins = ltiOrigins;

    // ---- Organizations (multi-tenant) ---------------------------------------
    /// <summary>Creating/deleting an organization is platform-super-admin only — everything
    /// else about running one (members, boards, AI settings) is OrgAdminController, which a
    /// super admin can also call for any org.</summary>
    [HttpGet("organizations")]
    public async Task<ActionResult<List<OrganizationDto>>> Organizations()
    {
        return await _db.Organizations.OrderBy(o => o.Name)
            .Select(o => new OrganizationDto(o.Id, o.Name, o.Slug, o.CreatedAt)).ToListAsync();
    }

    [HttpPost("organizations")]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization(AdminUpsertOrganizationDto dto)
    {
        var (name, slug, problem) = ParseOrganization(dto);
        if (problem is not null) return BadRequest(problem);
        if (await _db.Organizations.AnyAsync(o => o.Slug == slug)) return Conflict("That slug is already used.");

        var org = new Organization { Name = name, Slug = slug };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "create", "Organization", org.Id, org.Name);
        return new OrganizationDto(org.Id, org.Name, org.Slug, org.CreatedAt);
    }

    /// <summary>Rename an organization and/or change its slug. The slug only appears in URLs (/org-admin/&lt;slug&gt;,
    /// the leaderboard scope), nothing references it, so old links simply stop matching.</summary>
    [HttpPut("organizations/{id:int}")]
    public async Task<ActionResult<OrganizationDto>> UpdateOrganization(int id, AdminUpsertOrganizationDto dto)
    {
        var (name, slug, problem) = ParseOrganization(dto);
        if (problem is not null) return BadRequest(problem);
        var org = await _db.Organizations.FindAsync(id);
        if (org is null) return NotFound();
        if (await _db.Organizations.AnyAsync(o => o.Slug == slug && o.Id != id)) return Conflict("That slug is already used.");

        var before = $"{org.Name} ({org.Slug})";
        org.Name = name;
        org.Slug = slug;
        await _db.SaveChangesAsync();
        if (before != $"{org.Name} ({org.Slug})")
            await _audit.RecordAsync(UserId, ActorEmail, "update", "Organization", org.Id, $"{before} -> {org.Name} ({org.Slug})");
        return new OrganizationDto(org.Id, org.Name, org.Slug, org.CreatedAt);
    }

    private static (string Name, string Slug, string? Problem) ParseOrganization(AdminUpsertOrganizationDto dto)
    {
        var name = (dto.Name ?? "").Trim();
        var slug = (dto.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0 || slug.Length == 0) return (name, slug, "Name and slug are required.");
        if (name.Length > 160) return (name, slug, "Name is too long (160 characters max).");
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{0,63}$"))
            return (name, slug, "Slug can only use lowercase letters, digits and hyphens (64 characters max).");
        return (name, slug, null);
    }

    [HttpDelete("organizations/{id:int}")]
    public async Task<IActionResult> DeleteOrganization(int id)
    {
        var org = await _db.Organizations.FindAsync(id);
        if (org is null) return NotFound();
        _db.Organizations.Remove(org);   // memberships/AiSettings cascade; boards/LtiPlatforms just detach (SetNull)
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "Organization", id, org.Name);
        return NoContent();
    }

    // ---- LTI 1.3 platform registry ------------------------------------------
    /// <summary>Values an LMS admin needs to register BeeCoding as an external tool.</summary>
    [HttpGet("lti-platforms/tool-config")]
    public ActionResult<AdminLtiToolConfigDto> LtiToolConfig()
    {
        string Abs(string path) => $"{Request.Scheme}://{Request.Host}{Url.Content("~" + path)}";
        return new AdminLtiToolConfigDto(
            LoginInitiationUrl: Abs("/lti/login"),
            LaunchUrl: Abs("/lti/launch"),
            JwksUrl: Abs("/lti/jwks"),
            DeepLinkingUrl: Abs("/lti/launch"));   // same endpoint — discriminated by message_type
    }

    [HttpGet("lti-platforms")]
    public async Task<ActionResult<List<AdminLtiPlatformDto>>> LtiPlatforms()
    {
        return await _db.LtiPlatforms.Include(p => p.Organization).OrderBy(p => p.Name).Select(p => new AdminLtiPlatformDto(
            p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt,
            p.OrganizationId, p.Organization != null ? p.Organization.Name : null))
            .ToListAsync();
    }

    [HttpPost("lti-platforms")]
    public async Task<ActionResult<AdminLtiPlatformDto>> CreateLtiPlatform(AdminUpsertLtiPlatformDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Issuer) || string.IsNullOrWhiteSpace(dto.ClientId))
            return BadRequest("Issuer and Client ID are required.");
        var p = new LtiPlatform
        {
            Name = dto.Name.Trim(), Issuer = dto.Issuer.Trim(), ClientId = dto.ClientId.Trim(),
            DeploymentIds = dto.DeploymentIds.Trim(), AuthLoginUrl = dto.AuthLoginUrl.Trim(),
            AuthTokenUrl = dto.AuthTokenUrl.Trim(), JwksUrl = dto.JwksUrl.Trim(), Enabled = dto.Enabled,
            OrganizationId = dto.OrganizationId,
        };
        _db.LtiPlatforms.Add(p);
        await _db.SaveChangesAsync();
        await RefreshLtiOriginsAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "create", "LtiPlatform", p.Id, p.Name);
        var orgName = dto.OrganizationId is int oid ? await _db.Organizations.Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync() : null;
        return new AdminLtiPlatformDto(p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt, p.OrganizationId, orgName);
    }

    [HttpPut("lti-platforms/{id:int}")]
    public async Task<ActionResult<AdminLtiPlatformDto>> UpdateLtiPlatform(int id, AdminUpsertLtiPlatformDto dto)
    {
        var p = await _db.LtiPlatforms.FindAsync(id);
        if (p is null) return NotFound();
        p.Name = dto.Name.Trim(); p.Issuer = dto.Issuer.Trim(); p.ClientId = dto.ClientId.Trim();
        p.DeploymentIds = dto.DeploymentIds.Trim(); p.AuthLoginUrl = dto.AuthLoginUrl.Trim();
        p.AuthTokenUrl = dto.AuthTokenUrl.Trim(); p.JwksUrl = dto.JwksUrl.Trim(); p.Enabled = dto.Enabled;
        p.OrganizationId = dto.OrganizationId;
        await _db.SaveChangesAsync();
        await RefreshLtiOriginsAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "update", "LtiPlatform", p.Id, p.Name);
        var orgName = dto.OrganizationId is int oid ? await _db.Organizations.Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync() : null;
        return new AdminLtiPlatformDto(p.Id, p.Name, p.Issuer, p.ClientId, p.DeploymentIds, p.AuthLoginUrl, p.AuthTokenUrl, p.JwksUrl, p.Enabled, p.CreatedAt, p.OrganizationId, orgName);
    }

    [HttpDelete("lti-platforms/{id:int}")]
    public async Task<IActionResult> DeleteLtiPlatform(int id)
    {
        var p = await _db.LtiPlatforms.FindAsync(id);
        if (p is null) return NotFound();
        _db.LtiPlatforms.Remove(p);
        await _db.SaveChangesAsync();
        await RefreshLtiOriginsAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "delete", "LtiPlatform", id, p.Name);
        return NoContent();
    }

    /// <summary>Re-reads every enabled LTI platform's issuer into LtiPlatformOriginsCache —
    /// call after any write, so the CSP's frame-ancestors reflects the change immediately
    /// instead of only after a restart.</summary>
    private async Task RefreshLtiOriginsAsync() =>
        _ltiOrigins.Set(await _db.LtiPlatforms.Where(p => p.Enabled).Select(p => p.Issuer).ToListAsync());

}
