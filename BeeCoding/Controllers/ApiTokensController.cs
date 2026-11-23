using System.Security.Cryptography;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>Account → "Connect an AI assistant": a teacher's personal access tokens for <c>/mcp</c>. Creating one asks for the password
/// (a stolen session must not be able to mint a credential that outlives it); the text is shown once; at most 5, valid 90 days.</summary>
[Route("api/me/api-tokens")]
[Authorize]
public class ApiTokensController(AppDbContext db, PasswordService pw, LoginThrottle throttle, AuditLog audit, IConfiguration cfg) : ApiControllerBase
{
    private const int MaxTokens = 5;
    private const int Days = 90;

    public record CreateDto(string Name, string Password);
    public record TokenDto(int Id, string Name, DateTime CreatedAt, DateTime ExpiresAt, DateTime? LastUsedAt);

    private bool Allowed => cfg.GetValue("Mcp:Enabled", true) && CurrentRole == nameof(UserRole.Teacher);

    [HttpGet]
    public async Task<ActionResult<List<TokenDto>>> List()
    {
        if (!Allowed) return NotFound();
        return await db.ApiTokens.Where(t => t.UserId == UserId).OrderBy(t => t.CreatedAt)
            .Select(t => new TokenDto(t.Id, t.Name, t.CreatedAt, t.ExpiresAt, t.LastUsedAt)).ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create(CreateDto dto)
    {
        if (!Allowed) return NotFound();
        var name = (dto.Name ?? "").Trim();
        if (name.Length is 0 or > 80) return BadRequest("Give the token a name (up to 80 characters).");
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var key = $"api-token:{UserId}";
        if (!throttle.TryBegin(ip, key, strict: true)) return StatusCode(StatusCodes.Status429TooManyRequests, "Too many attempts. Try again in a few minutes.");
        if (pw.Reauth(user, dto.Password) is string err) return BadRequest(err);
        throttle.RecordSuccess(ip, key);
        if (await db.ApiTokens.CountAsync(t => t.UserId == UserId) >= MaxTokens) return BadRequest($"You can have at most {MaxTokens} tokens. Revoke one first.");

        var token = "bcm_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var row = new ApiToken { UserId = UserId, Name = name, TokenHash = McpController.Hash(token), ExpiresAt = DateTime.UtcNow.AddDays(Days) };
        db.ApiTokens.Add(row);
        await db.SaveChangesAsync();
        await audit.RecordAsync(UserId, ActorEmail, "api-token-create", "api-token", row.Id, name);
        return new { id = row.Id, name = row.Name, token, expiresAt = row.ExpiresAt };
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id)
    {
        if (!Allowed) return NotFound();
        var row = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (row is null) return NotFound();
        db.ApiTokens.Remove(row);
        await db.SaveChangesAsync();
        await audit.RecordAsync(UserId, ActorEmail, "api-token-revoke", "api-token", id, row.Name);
        return NoContent();
    }
}
