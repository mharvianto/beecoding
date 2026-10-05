using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Sign in with Google. A browser round-trip (<c>start</c> → Google → <c>callback</c>) ends in a redirect to
/// the SPA: straight in when the Google identity is already linked, to a password prompt when the email
/// already has an account (never linked on email alone — a pre-registered account could be hijacked), or to
/// sign-up when it is new. Accounts with a second factor still finish it.
/// </summary>
[Route("api/auth/google")]
public class GoogleController(AppDbContext db, GoogleAuthService google, MfaService mfa,
    PasswordService pw, LoginThrottle throttle, MeDtoBuilder me, IConfiguration cfg) : ApiControllerBase
{
    private const string Provider = "google";
    private const string CookieName = "beecoding.google";
    private const string PurposeOAuth = "google-oauth";
    private const string PurposePending = "google-pending";

    private readonly AppDbContext _db = db;
    private readonly GoogleAuthService _google = google;
    private readonly MfaService _mfa = mfa;
    private readonly PasswordService _pw = pw;
    private readonly LoginThrottle _throttle = throttle;
    private readonly MeDtoBuilder _me = me;
    private readonly IConfiguration _cfg = cfg;

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
    private string RedirectUri => PublicUrl.Build(_cfg, Request, "/api/auth/google/callback");

    private record OAuthState(string State, string Nonce, string Verifier, string Mode, int UserId, string Return);
    private record Pending(string Sub, string Email, string Name);

    // app-relative path only — never an absolute or protocol-relative URL
    private static string SafeReturn(string? r) =>
        !string.IsNullOrEmpty(r) && r[0] == '/' && !r.StartsWith("//") && !r.Contains('\\') && r.Length < 300 ? r : "/boards";

    private IActionResult ToApp(string path) => Redirect(Request.PathBase + path);

    private IActionResult Fail(string message, string mode) =>
        ToApp((mode == "link" ? "/account/security?googleError=" : "/login?error=") + Uri.EscapeDataString(message));

    // ---------------------------------------------------------------- browser round-trip

    [HttpGet("start")]
    [AllowAnonymous]
    public IActionResult Start([FromQuery] string? mode, [FromQuery] string? r)
    {
        if (!_google.Enabled) return NotFound();
        var link = mode == "link";
        if (link && User.Identity?.IsAuthenticated != true) return ToApp("/login");

        var (verifier, challenge) = GoogleAuthService.NewPkce();
        var st = new OAuthState(GoogleAuthService.RandomToken(), GoogleAuthService.RandomToken(), verifier,
            link ? "link" : "login", link ? UserId : 0, SafeReturn(r));
        Response.Cookies.Append(CookieName, _mfa.ProtectState(PurposeOAuth, 0, JsonSerializer.Serialize(st)), new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(5), Path = Request.PathBase + "/api/auth/google",
        });
        return Redirect(_google.AuthorizeUrl(RedirectUri, st.State, st.Nonce, challenge));
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!_google.Enabled) return NotFound();

        var raw = _mfa.ReadState(PurposeOAuth, 0, Request.Cookies[CookieName]);
        Response.Cookies.Delete(CookieName, new CookieOptions { Path = Request.PathBase + "/api/auth/google" });
        var st = raw is null ? null : JsonSerializer.Deserialize<OAuthState>(raw);
        var mode = st?.Mode ?? "login";
        if (st is null || error is not null || string.IsNullOrEmpty(code) || state != st.State)
            return Fail("Google sign-in was cancelled or timed out. Try again.", mode);

        var who = await _google.ExchangeAsync(code, RedirectUri, st.Verifier);
        if (who is null || who.Nonce != st.Nonce) return Fail("Google sign-in could not be verified. Try again.", mode);

        if (mode == "link") return await LinkToSessionAsync(st, who);

        var known = await _db.ExternalLogins.Include(x => x.User).FirstOrDefaultAsync(x => x.Provider == Provider && x.Subject == who.Subject);
        if (known?.User is { DeletedAt: null } linked)
        {
            known.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            if (await _mfa.ChallengeAsync(linked) is { } c)
                return ToApp($"/login?mfa={Uri.EscapeDataString(c.Ticket)}&r={Uri.EscapeDataString(st.Return)}");
            await CookieSignIn.SignInAsync(HttpContext, linked);
            return ToApp(st.Return);
        }

        if (!who.EmailVerified) return Fail("Google has not verified that email address, so it can't be used to sign in.", mode);

        var pending = Uri.EscapeDataString(_mfa.ProtectState(PurposePending, 0, JsonSerializer.Serialize(new Pending(who.Subject, who.Email, who.Name))));
        var exists = await _db.Users.AnyAsync(u => u.Email == who.Email && u.DeletedAt == null);
        return ToApp($"/{(exists ? "login?link=" : "register?google=")}{pending}&r={Uri.EscapeDataString(st.Return)}");
    }

    /// <summary>Connect Google to the account that is signed in right now (from Account).</summary>
    private async Task<IActionResult> LinkToSessionAsync(OAuthState st, GoogleIdentity who)
    {
        if (User.Identity?.IsAuthenticated != true || UserId != st.UserId) return Fail("Sign in again, then connect Google.", "link");
        var other = await _db.ExternalLogins.FirstOrDefaultAsync(x => x.Provider == Provider && x.Subject == who.Subject);
        if (other is not null && other.UserId != st.UserId) return Fail("That Google account is already connected to another BeeCoding account.", "link");

        var mine = await _db.ExternalLogins.FirstOrDefaultAsync(x => x.Provider == Provider && x.UserId == st.UserId);
        if (mine is null) _db.ExternalLogins.Add(new ExternalLogin { UserId = st.UserId, Provider = Provider, Subject = who.Subject, Email = who.Email });
        else { mine.Subject = who.Subject; mine.Email = who.Email; }
        await _db.SaveChangesAsync();
        return ToApp("/account/security?google=linked");
    }

    // ---------------------------------------------------------------- finishing from the SPA

    private Pending? ReadPending(string? ticket)
    {
        var raw = _mfa.ReadState(PurposePending, 0, ticket);
        return raw is null ? null : JsonSerializer.Deserialize<Pending>(raw);
    }

    [HttpPost("pending")]
    [AllowAnonymous]
    public ActionResult<GooglePendingDto> PendingInfo(GoogleTicketDto dto)
    {
        var p = ReadPending(dto.Ticket);
        return p is null ? Unauthorized("This Google sign-in expired. Start again.") : new GooglePendingDto(p.Email, p.Name);
    }

    /// <summary>An account with this email already exists: prove it is theirs with its password, then connect Google.</summary>
    [HttpPost("link")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Link(GoogleLinkDto dto)
    {
        var p = ReadPending(dto.Ticket);
        if (p is null) return Unauthorized("This Google sign-in expired. Start again.");
        if (_throttle.IsBlocked(ClientIp, p.Email))
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again in a few minutes.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == p.Email && u.DeletedAt == null);
        if (user is null) return Unauthorized("This Google sign-in expired. Start again.");
        if (_pw.Reauth(user, dto.Password) is string err)
        {
            _throttle.RecordFailure(ClientIp, p.Email);
            return Unauthorized(err);
        }
        _throttle.RecordSuccess(ClientIp, p.Email);

        if (await _db.ExternalLogins.AnyAsync(x => x.Provider == Provider && x.Subject == p.Sub && x.UserId != user.Id))
            return Conflict("That Google account is already connected to another BeeCoding account.");
        if (!await _db.ExternalLogins.AnyAsync(x => x.Provider == Provider && x.Subject == p.Sub))
            _db.ExternalLogins.Add(new ExternalLogin { UserId = user.Id, Provider = Provider, Subject = p.Sub, Email = p.Email, LastLoginAt = DateTime.UtcNow });
        user.EmailVerifiedAt ??= DateTime.UtcNow;   // Google vouched for this very address
        await _db.SaveChangesAsync();

        if (await _mfa.ChallengeAsync(user) is { } challenge) return challenge;
        await CookieSignIn.SignInAsync(HttpContext, user);
        return await _me.BuildAsync(user);
    }

    /// <summary>A new person: pick a role (and teacher code if needed) and an account is created, signed in, email verified.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> Register(GoogleRegisterDto dto)
    {
        var p = ReadPending(dto.Ticket);
        if (p is null) return Unauthorized("This Google sign-in expired. Start again.");

        var name = (string.IsNullOrWhiteSpace(dto.DisplayName) ? p.Name : dto.DisplayName).Trim();
        if (name.Length < 2) return BadRequest("Display name must be at least 2 characters.");
        if (name.Length > 40) name = name[..40];

        var (role, roleError) = SignupRole.Resolve(_cfg, dto.Role, dto.TeacherCode);
        if (role is null) return BadRequest(roleError);
        if (await _db.Users.AnyAsync(u => u.Email == p.Email)) return Conflict("An account with that email already exists. Sign in and connect Google from Account.");
        if (await _db.ExternalLogins.AnyAsync(x => x.Provider == Provider && x.Subject == p.Sub)) return Conflict("That Google account is already connected.");

        var user = new User { Email = p.Email, DisplayName = name, Role = role.Value, EmailVerifiedAt = DateTime.UtcNow, HasPassword = false };
        user.PasswordHash = _pw.HashRandom(user);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.ExternalLogins.Add(new ExternalLogin { UserId = user.Id, Provider = Provider, Subject = p.Sub, Email = p.Email, LastLoginAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await CookieSignIn.SignInAsync(HttpContext, user);
        return await _me.BuildAsync(user);
    }

    // ---------------------------------------------------------------- account settings

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<GoogleStatusDto>> Status()
    {
        var link = await _db.ExternalLogins.FirstOrDefaultAsync(x => x.Provider == Provider && x.UserId == UserId);
        return new GoogleStatusDto(link is not null, link?.Email);
    }

    [HttpPost("unlink")]
    [Authorize]
    public async Task<IActionResult> Unlink(MfaPasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (_pw.Reauth(user, dto.Password) is string err) return BadRequest(err);   // also guarantees a password exists to fall back on
        _db.ExternalLogins.RemoveRange(_db.ExternalLogins.Where(x => x.Provider == Provider && x.UserId == UserId));
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
