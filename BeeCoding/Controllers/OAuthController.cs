using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// OAuth 2.1 for <c>/mcp</c> (the MCP authorization flow): lets a teacher click "Connect" in claude.ai instead of pasting a token.
/// Authorization code + PKCE (S256 only), public clients that register themselves (RFC 7591), consent given on a BeeCoding page
/// while signed in (<c>OAuthConsent.vue</c>). What comes out is an ordinary <see cref="ApiToken"/> (1-hour access token + a refresh
/// token that rotates at every use), so every rule of the hand-made tokens applies: teachers only, revoked by a password change,
/// listed and revocable in Account. Discovery follows RFC 9728 / 8414; a sub-path deployment also needs the well-known paths at the
/// host root forwarded (DEPLOY.md).
/// </summary>
[ApiController]
[Authorize]   // only the two api/oauth/* actions need a signed-in teacher; the protocol endpoints are marked [AllowAnonymous] one by one
public class OAuthController(AppDbContext db, ICounterStore counters, IConfiguration cfg) : ApiControllerBase
{
    private const string Scope = "mcp:read";
    private static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(5), AccessLife = TimeSpan.FromHours(1), RefreshLife = TimeSpan.FromDays(60);
    private static readonly string[] DefaultRedirects = ["https://claude.ai/api/mcp/auth_callback", "https://claude.com/api/mcp/auth_callback"];
    private static readonly Regex Pkce = new("^[A-Za-z0-9_-]{43,128}$", RegexOptions.Compiled);
    private static readonly Regex Verifier = new("^[A-Za-z0-9._~-]{43,128}$", RegexOptions.Compiled);

    private string Base => PublicUrl.Build(cfg, Request, "");
    private bool Enabled => cfg.GetValue("Mcp:Enabled", true);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";

    // ---------- discovery ----------

    [AllowAnonymous]
    [HttpGet(".well-known/oauth-protected-resource")]
    public IActionResult ResourceMetadata() => !Enabled ? NotFound() : Ok(new JsonObject
    {
        ["resource"] = Base + "/mcp",
        ["authorization_servers"] = new JsonArray(Base),
        ["bearer_methods_supported"] = new JsonArray("header"),
        ["scopes_supported"] = new JsonArray(Scope),
    });

    [AllowAnonymous]
    [HttpGet(".well-known/oauth-authorization-server")]
    [HttpGet(".well-known/openid-configuration")]   // the form clients try for an issuer that has a path
    public IActionResult ServerMetadata() => !Enabled ? NotFound() : Ok(new JsonObject
    {
        ["issuer"] = Base,
        ["authorization_endpoint"] = Base + "/oauth/authorize",
        ["token_endpoint"] = Base + "/oauth/token",
        ["registration_endpoint"] = Base + "/oauth/register",
        ["response_types_supported"] = new JsonArray("code"),
        ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token"),
        ["code_challenge_methods_supported"] = new JsonArray("S256"),
        ["token_endpoint_auth_methods_supported"] = new JsonArray("none"),
        ["scopes_supported"] = new JsonArray(Scope),
    });

    // ---------- redirect allow-list ----------

    private bool RedirectAllowed(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Fragment.Length > 0 || u.UserInfo.Length > 0 || uri!.Length > 500) return false;
        if (u.Scheme == Uri.UriSchemeHttp) return u.Host is "localhost" or "127.0.0.1" or "[::1]";   // a command-line client listening on this machine
        var allowed = cfg.GetSection("Mcp:OAuthRedirects").Get<string[]>() is { Length: > 0 } c ? c : DefaultRedirects;
        return u.Scheme == Uri.UriSchemeHttps && allowed.Contains(u.GetLeftPart(UriPartial.Path), StringComparer.OrdinalIgnoreCase) && u.Query.Length == 0;
    }

    private static JsonResult Err(string error, string? description = null, int status = 400) =>
        new(new JsonObject { ["error"] = error, ["error_description"] = description }) { StatusCode = status };

    // ---------- registration (RFC 7591) ----------

    [AllowAnonymous]
    [HttpPost("oauth/register")]
    public async Task<IActionResult> Register([FromBody] JsonObject body)
    {
        if (!Enabled) return NotFound();
        if (counters.Increment("oauth-reg:" + Ip, TimeSpan.FromHours(1)) > 20) return Err("invalid_client_metadata", "Too many registrations.", 429);

        var uris = (body["redirect_uris"] as JsonArray)?.Select(n => n?.GetValue<string>()).ToList() ?? [];
        if (uris.Count is 0 or > 5 || uris.Any(u => !RedirectAllowed(u))) return Err("invalid_redirect_uri", "A redirect address is not allowed.");
        var method = body["token_endpoint_auth_method"]?.GetValue<string>();
        if (method is not (null or "none")) return Err("invalid_client_metadata", "Only public clients (token_endpoint_auth_method none) are supported.");

        var cutoff = DateTime.UtcNow.AddDays(-1);   // registrations nobody ever authorized do not pile up (a flood cannot hold the 2000 slots for long)
        var dead = DateTime.UtcNow.AddDays(-30);    // connections whose refresh (or, for a hand-made token, access) lifetime ended a month ago
        await db.ApiTokens.Where(t => (t.RefreshExpiresAt ?? t.ExpiresAt) < dead).ExecuteDeleteAsync();
        await db.OAuthClients.Where(c => c.CreatedAt < cutoff && !db.ApiTokens.Any(t => t.ClientId == c.ClientId)).ExecuteDeleteAsync();
        if (await db.OAuthClients.CountAsync() >= 2000) return Err("invalid_client_metadata", "Registration is full. Try later.", 503);

        var name = Regex.Replace(body["client_name"]?.GetValue<string>() ?? "", @"\p{C}", "").Trim();
        if (name.Length == 0) name = "AI assistant";
        var c = new OAuthClient { ClientId = Random(18), Name = name.Length > 80 ? name[..80] : name, RedirectUris = string.Join('\n', uris) };
        db.OAuthClients.Add(c);
        await db.SaveChangesAsync();
        return new JsonResult(new JsonObject
        {
            ["client_id"] = c.ClientId, ["client_name"] = c.Name, ["client_id_issued_at"] = new DateTimeOffset(c.CreatedAt).ToUnixTimeSeconds(),
            ["redirect_uris"] = new JsonArray(uris.Select(u => (JsonNode?)u).ToArray()),
            ["token_endpoint_auth_method"] = "none",
            ["grant_types"] = new JsonArray("authorization_code", "refresh_token"), ["response_types"] = new JsonArray("code"),
        }) { StatusCode = 201 };
    }

    // ---------- authorization ----------

    /// <summary>The browser lands here from the assistant. Bad client or redirect → a plain error page (never redirect to an unchecked address);
    /// otherwise on to the consent page of the app, which needs a signed-in teacher.</summary>
    [AllowAnonymous]
    [HttpGet("oauth/authorize")]
    public async Task<IActionResult> Authorize([FromQuery(Name = "client_id")] string? clientId, [FromQuery(Name = "redirect_uri")] string? redirectUri)
    {
        if (!Enabled) return NotFound();
        if (await FindClientAsync(clientId, redirectUri) is null) return BadRequest("Unknown application or redirect address.");
        return Redirect(PublicUrl.Build(cfg, Request, "/oauth/consent" + Request.QueryString));
    }

    private async Task<OAuthClient?> FindClientAsync(string? clientId, string? redirectUri)
    {
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri)) return null;
        var c = await db.OAuthClients.FirstOrDefaultAsync(x => x.ClientId == clientId);
        return c is not null && c.RedirectUris.Split('\n').Contains(redirectUri) && RedirectAllowed(redirectUri) ? c : null;
    }

    [HttpGet("api/oauth/client")]
    public async Task<IActionResult> ClientInfo([FromQuery] string? clientId, [FromQuery] string? redirectUri)
    {
        var c = Enabled ? await FindClientAsync(clientId, redirectUri) : null;
        return c is null ? NotFound() : Ok(new { name = c.Name, host = new Uri(redirectUri!).Host, teacher = CurrentRole == nameof(UserRole.Teacher) });
    }

    public record DecisionDto(string ClientId, string RedirectUri, string? State, string CodeChallenge, string? CodeChallengeMethod, string? Resource, bool Approve);

    /// <summary>The consent page's answer. Same-origin POST with the session cookie (CSRF-checked like every other), so only a click on
    /// our own page can grant access.</summary>
    [HttpPost("api/oauth/authorize")]
    public async Task<IActionResult> Decide(DecisionDto dto)
    {
        if (!Enabled) return NotFound();
        if (CurrentRole != nameof(UserRole.Teacher)) return StatusCode(403, "Only teachers can connect an assistant.");
        var client = await FindClientAsync(dto.ClientId, dto.RedirectUri);
        if (client is null) return BadRequest("Unknown application or redirect address.");

        string Back(params (string k, string? v)[] q)
        {
            var url = dto.RedirectUri;
            foreach (var (k, v) in q) if (v is not null) url = QueryHelpers.AddQueryString(url, k, v);
            return url;
        }
        if (!dto.Approve) return Ok(new { redirectTo = Back(("error", "access_denied"), ("state", dto.State)) });
        if (dto.CodeChallengeMethod != "S256" || !Pkce.IsMatch(dto.CodeChallenge ?? ""))
            return Ok(new { redirectTo = Back(("error", "invalid_request"), ("error_description", "PKCE with S256 is required"), ("state", dto.State)) });
        if (dto.Resource is { Length: > 0 } && dto.Resource.TrimEnd('/') != Base + "/mcp")
            return Ok(new { redirectTo = Back(("error", "invalid_target"), ("state", dto.State)) });

        var now = DateTime.UtcNow;
        await db.OAuthCodes.Where(c => c.ExpiresAt < now).ExecuteDeleteAsync();
        var code = Random(32);
        db.OAuthCodes.Add(new OAuthCode { CodeHash = McpController.Hash(code), ClientId = client.ClientId, UserId = UserId, RedirectUri = dto.RedirectUri, CodeChallenge = dto.CodeChallenge, ExpiresAt = now + CodeLife });
        await db.SaveChangesAsync();
        return Ok(new { redirectTo = Back(("code", code), ("state", dto.State)) });
    }

    // ---------- token ----------

    [AllowAnonymous]
    [HttpPost("oauth/token")]
    public async Task<IActionResult> Token()
    {
        if (!Enabled) return NotFound();
        Response.Headers.CacheControl = "no-store";
        if (!Request.HasFormContentType) return Err("invalid_request", "Send application/x-www-form-urlencoded.");
        // count the attempt BEFORE checking it, as everywhere else (LoginThrottle): parallel guesses must not all pass
        var key = "oauth-bad:" + Ip;
        if (counters.Increment(key, TimeSpan.FromMinutes(10)) > 60) return Err("invalid_request", "Too many attempts.", 429);

        var f = Request.Form;
        string P(string n) => f[n].ToString();
        var clientId = P("client_id");
        var client = await db.OAuthClients.FirstOrDefaultAsync(c => c.ClientId == clientId);
        if (client is null) return Err("invalid_client", null, 401);
        var now = DateTime.UtcNow;

        User? user; ApiToken? existing = null;
        switch (P("grant_type"))
        {
            case "authorization_code":
            {
                var ch = McpController.Hash(P("code"));
                var row = await db.OAuthCodes.FirstOrDefaultAsync(c => c.CodeHash == ch);
                // one use only: the delete decides, and it happens before the verifier is looked at, so a wrong guess burns the code
                if (row is null || await db.OAuthCodes.Where(c => c.Id == row.Id).ExecuteDeleteAsync() != 1) return Err("invalid_grant");
                if (row.ExpiresAt < now || row.ClientId != client.ClientId || row.RedirectUri != P("redirect_uri")) return Err("invalid_grant");
                var verifier = P("code_verifier");
                var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                if (!Verifier.IsMatch(verifier) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(row.CodeChallenge)))
                    return Err("invalid_grant", "PKCE verification failed.");
                user = await db.Users.FindAsync(row.UserId);
                break;
            }
            case "refresh_token":
            {
                var rh = McpController.Hash(P("refresh_token"));
                existing = await db.ApiTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.RefreshHash == rh && t.ClientId == client.ClientId && t.RefreshExpiresAt > now);
                if (existing is null) return Err("invalid_grant");
                user = existing.User;
                break;
            }
            default: return Err("unsupported_grant_type");
        }
        if (user is null || user.DeletedAt != null || user.IsSystem || user.Role != UserRole.Teacher) return Err("invalid_grant");

        var access = "bcm_" + Random(32);
        var refresh = "bcr_" + Random(32);
        if (existing is null)
        {
            await db.ApiTokens.Where(t => t.UserId == user.Id && t.ClientId == client.ClientId).ExecuteDeleteAsync();   // one connection per person and app
            db.ApiTokens.Add(new ApiToken
            {
                UserId = user.Id, Name = client.Name, ClientId = client.ClientId, TokenHash = McpController.Hash(access), ExpiresAt = now + AccessLife,
                RefreshHash = McpController.Hash(refresh), RefreshExpiresAt = now + RefreshLife,
            });
            await db.SaveChangesAsync();
        }
        else
        {
            var old = existing.RefreshHash;
            var th = McpController.Hash(access); var rh2 = McpController.Hash(refresh);
            // rotate; two refreshes at once with the same token: only the first one wins
            if (await db.ApiTokens.Where(t => t.Id == existing.Id && t.RefreshHash == old).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.TokenHash, th).SetProperty(t => t.RefreshHash, rh2)
                    .SetProperty(t => t.ExpiresAt, now + AccessLife).SetProperty(t => t.RefreshExpiresAt, now + RefreshLife)) != 1) return Err("invalid_grant");
        }
        counters.Remove(key);
        return new JsonResult(new JsonObject
        {
            ["access_token"] = access, ["token_type"] = "Bearer", ["expires_in"] = (int)AccessLife.TotalSeconds,
            ["refresh_token"] = refresh, ["scope"] = Scope,
        });
    }

    private static string Random(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
