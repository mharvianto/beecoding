using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Mcp;
using BeeCoding.Services.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// The MCP endpoint (<c>POST /mcp</c>, see <see cref="McpServer"/>). It is signed in ONLY by a personal access token in
/// <c>Authorization: Bearer</c>; the session cookie is ignored, so a page on another site cannot use a teacher's browser to call it.
/// <c>Mcp:Enabled=false</c> turns it, and the token screen, off.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("mcp")]
public class McpController(AppDbContext db, McpServer server, AdminAccess admin, ICounterStore counters, IConfiguration cfg) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly TimeSpan BadWindow = TimeSpan.FromMinutes(10);

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    [HttpGet]
    public IActionResult NoStream() => cfg.GetValue("Mcp:Enabled", true) ? StatusCode(StatusCodes.Status405MethodNotAllowed) : NotFound();

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Post([FromBody] JsonNode? message)
    {
        if (!cfg.GetValue("Mcp:Enabled", true)) return NotFound();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var header = Request.Headers.Authorization.ToString();
        var bearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : "";
        // count a guess BEFORE looking it up, like LoginThrottle: a burst of parallel guesses must not all pass
        if (bearer.Length == 0 || counters.Increment("mcp-bad:" + ip, BadWindow) > 30) return Denied();

        var hash = Hash(bearer);
        var now = DateTime.UtcNow;
        var tok = await db.ApiTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash);
        var u = tok?.User;
        if (tok is null || u is null || tok.ExpiresAt <= now || u.DeletedAt != null || u.IsSystem || u.Role == UserRole.Student) return Denied();
        counters.Remove("mcp-bad:" + ip);

        if (counters.Increment("mcp:" + tok.Id, TimeSpan.FromMinutes(1)) > 60) return StatusCode(StatusCodes.Status429TooManyRequests, "Too many requests.");
        if (tok.LastUsedAt is null || now - tok.LastUsedAt > TimeSpan.FromMinutes(5))
        {
            tok.LastUsedAt = now;
            await db.SaveChangesAsync();
        }

        var reply = await server.HandleAsync(message, new McpCaller(u.Id, u.Email, admin.IsAdminEmail(u.Email)));
        return reply is null ? Accepted() : Ok(reply);
    }

    private IActionResult Denied()
    {
        // points an OAuth-capable client (claude.ai) at where to start the "Connect" flow (RFC 9728)
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{PublicUrl.Build(cfg, Request, "/.well-known/oauth-protected-resource")}\"";
        return Unauthorized();
    }
}
