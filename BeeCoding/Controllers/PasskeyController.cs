using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Passkeys as a passwordless sign-in ("Sign in with a passkey" on the login page) plus the account
/// settings that manage them. A passkey sign-in needs user verification, so it counts as two factors
/// and goes straight to a session — it does not ask for an authenticator-app code afterwards.
/// </summary>
[Route("api/auth/passkeys")]
public class PasskeyController(AppDbContext db, PasswordService pw, MfaService mfa, PasskeyService passkeys,
    LoginThrottle throttle, MeDtoBuilder me) : ApiControllerBase
{
    private const string StateLogin = "passkey-login";
    private const string StateRegister = "passkey-register";

    private readonly AppDbContext _db = db;
    private readonly PasswordService _pw = pw;
    private readonly MfaService _mfa = mfa;       // only for its sealed, short-lived state blobs
    private readonly PasskeyService _passkeys = passkeys;
    private readonly LoginThrottle _throttle = throttle;
    private readonly MeDtoBuilder _me = me;

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
    private string ThrottleKey => $"passkey:{ClientIp}";

    // ======================== signing in ========================

    [HttpPost("login/options")]
    [AllowAnonymous]
    public IActionResult LoginOptions()
    {
        if (!_passkeys.Enabled) return NotFound();
        var options = _passkeys.LoginOptions();
        return Ok(new { options, state = _mfa.ProtectState(StateLogin, 0, options.ToJson()) });
    }

    public record LoginDto(string State, AuthenticatorAssertionRawResponse Response);

    [HttpPost("login/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> LoginVerify(LoginDto dto)
    {
        if (!_passkeys.Enabled) return NotFound();
        if (_throttle.IsBlocked(ClientIp, ThrottleKey))
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again in a few minutes.");

        var json = _mfa.ReadState(StateLogin, 0, dto.State);
        if (json is null) return BadRequest("This passkey request expired. Try again.");
        var options = AssertionOptions.FromJson(json);

        var stored = await _db.UserPasskeys.Include(p => p.User).FirstOrDefaultAsync(p => p.CredentialId == dto.Response.RawId);
        if (stored?.User is not { DeletedAt: null } user)
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey);
            return Unauthorized("That passkey isn't registered here. Sign in with your password instead.");
        }
        try
        {
            var result = await _passkeys.CompleteLoginAsync(dto.Response, options, stored);
            stored.SignCount = result.SignCount;
            stored.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        catch (Fido2VerificationException)
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey);
            return Unauthorized("Passkey verification failed.");
        }

        _throttle.RecordSuccess(ClientIp, ThrottleKey);
        await CookieSignIn.SignInAsync(HttpContext, user);
        return await _me.BuildAsync(user);
    }

    // ======================== account settings ========================

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<PasskeyStatusDto>> Status()
    {
        var keys = await _db.UserPasskeys.Where(p => p.UserId == UserId).OrderBy(p => p.CreatedAt)
            .Select(p => new PasskeyDto(p.Id, p.Name, p.CreatedAt, p.LastUsedAt)).ToListAsync();
        return new PasskeyStatusDto(_passkeys.Enabled, keys);
    }

    [HttpPost("options")]
    [Authorize]
    public async Task<IActionResult> RegisterOptions()
    {
        if (!_passkeys.Enabled) return NotFound();
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        var existing = await _db.UserPasskeys.Where(p => p.UserId == user.Id).Select(p => p.CredentialId).ToListAsync();
        var options = _passkeys.CreationOptions(user, existing);
        return Ok(new { options, state = _mfa.ProtectState(StateRegister, user.Id, options.ToJson()) });
    }

    public record RegisterDto(string State, string? Name, AuthenticatorAttestationRawResponse Response);

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (!_passkeys.Enabled) return NotFound();
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();

        var json = _mfa.ReadState(StateRegister, user.Id, dto.State);
        if (json is null) return BadRequest("This request expired. Try again.");
        var options = CredentialCreateOptions.FromJson(json);

        Fido2NetLib.Objects.RegisteredPublicKeyCredential cred;
        try
        {
            cred = await _passkeys.CompleteRegistrationAsync(dto.Response, options,
                async id => !await _db.UserPasskeys.AnyAsync(p => p.CredentialId == id));
        }
        catch (Fido2VerificationException e) { return BadRequest($"Could not register the passkey: {e.Message}"); }

        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) name = "Passkey";
        if (name.Length > 80) name = name[..80];
        _db.UserPasskeys.Add(new UserPasskey
        {
            UserId = user.Id, CredentialId = cred.Id, PublicKey = cred.PublicKey, SignCount = cred.SignCount, Name = name,
        });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:int}")]
    [Authorize]
    public async Task<IActionResult> Rename(int id, PasskeyNameDto dto)
    {
        var key = await _db.UserPasskeys.FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId);
        if (key is null) return NotFound();
        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest("Give the passkey a name.");
        key.Name = name.Length > 80 ? name[..80] : name;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize]
    public async Task<IActionResult> Remove(int id, MfaPasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (_pw.Reauth(user, dto.Password) is string err) return BadRequest(err);
        var key = await _db.UserPasskeys.FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId);
        if (key is null) return NotFound();
        _db.UserPasskeys.Remove(key);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
