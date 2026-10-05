using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Second-factor sign-in (authenticator app, passkey, recovery code) and the account settings that
/// manage them. The anonymous endpoints take the ticket that <c>POST /api/auth/login</c> hands out
/// after a correct password; the authenticated ones need a session.
/// </summary>
[Route("api/auth/mfa")]
public class MfaController(AppDbContext db, PasswordService pw, MfaService mfa, PasskeyService passkeys,
    LoginThrottle throttle, AdminAccess admin) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly PasswordService _pw = pw;
    private readonly MfaService _mfa = mfa;
    private readonly PasskeyService _passkeys = passkeys;
    private readonly LoginThrottle _throttle = throttle;
    private readonly AdminAccess _admin = admin;

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
    private static string ThrottleKey(int userId) => $"mfa:{userId}";

    private const string StatePasskeyLogin = "passkey-login";
    private const string StatePasskeyRegister = "passkey-register";

    // ======================== finishing a login ========================

    private async Task<(User? User, ActionResult? Fail)> LoadForTicketAsync(string? ticket)
    {
        var uid = _mfa.ReadTicket(ticket);
        if (uid is null) return (null, Unauthorized("This sign-in expired. Enter your password again."));
        if (_throttle.IsBlocked(ClientIp, ThrottleKey(uid.Value)))
            return (null, StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again in a few minutes."));
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid.Value);
        if (user is null || user.DeletedAt is not null) return (null, Unauthorized("This sign-in expired. Enter your password again."));
        return (user, null);
    }

    private async Task<ActionResult<MeDto>> FinishLoginAsync(User user)
    {
        _throttle.RecordSuccess(ClientIp, ThrottleKey(user.Id));
        await CookieSignIn.SignInAsync(HttpContext, user);
        return await MeDtoBuilder.BuildAsync(_db, _admin, user);
    }

    /// <summary>Second step with an authenticator-app code or a recovery code.</summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> Verify(MfaVerifyDto dto)
    {
        var (user, fail) = await LoadForTicketAsync(dto.Ticket);
        if (user is null) return fail!;

        bool ok = dto.Method switch
        {
            "totp" => user.TotpEnabledAt != null && await _mfa.VerifyTotpAsync(user, dto.Code),
            "recovery" => await _mfa.UseRecoveryCodeAsync(user.Id, dto.Code),
            _ => false,
        };
        if (!ok)
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey(user.Id));
            return Unauthorized(dto.Method == "recovery" ? "That recovery code is wrong or already used." : "Wrong code. Check your authenticator app and try again.");
        }
        return await FinishLoginAsync(user);
    }

    /// <summary>Which second-factor methods an in-progress login can finish with (used when Google sign-in
    /// hands the browser a ticket by redirect instead of by JSON).</summary>
    [HttpPost("methods")]
    [AllowAnonymous]
    public async Task<ActionResult<string[]>> Methods(MfaTicketDto dto)
    {
        var (user, fail) = await LoadForTicketAsync(dto.Ticket);
        if (user is null) return fail!;
        var c = await _mfa.ChallengeAsync(user, _passkeys.Enabled);
        return c?.Methods ?? Array.Empty<string>();
    }

    /// <summary>Begin a passkey login: the challenge the browser must sign with one of the user's passkeys.</summary>
    [HttpPost("passkey/options")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyLoginOptions(MfaTicketDto dto)
    {
        if (!_passkeys.Enabled) return NotFound();
        var (user, fail) = await LoadForTicketAsync(dto.Ticket);
        if (user is null) return fail!;

        var ids = await _db.UserPasskeys.Where(p => p.UserId == user.Id).Select(p => p.CredentialId).ToListAsync();
        if (ids.Count == 0) return BadRequest("No passkey is registered for this account.");
        var options = _passkeys.AssertionOptions(ids);
        return Ok(new { options, state = _mfa.ProtectState(StatePasskeyLogin, user.Id, options.ToJson()) });
    }

    public record PasskeyLoginDto(string Ticket, string State, AuthenticatorAssertionRawResponse Response);

    [HttpPost("passkey/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> PasskeyLoginVerify(PasskeyLoginDto dto)
    {
        if (!_passkeys.Enabled) return NotFound();
        var (user, fail) = await LoadForTicketAsync(dto.Ticket);
        if (user is null) return fail!;

        var json = _mfa.ReadState(StatePasskeyLogin, user.Id, dto.State);
        if (json is null) return BadRequest("This passkey request expired. Try again.");
        var options = AssertionOptions.FromJson(json);

        var stored = await _db.UserPasskeys.FirstOrDefaultAsync(p => p.UserId == user.Id && p.CredentialId == dto.Response.RawId);
        if (stored is null)
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey(user.Id));
            return Unauthorized("That passkey is not registered for this account.");
        }
        try
        {
            var result = await _passkeys.CompleteAssertionAsync(dto.Response, options, stored);
            stored.SignCount = result.SignCount;
            stored.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        catch (Fido2VerificationException)
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey(user.Id));
            return Unauthorized("Passkey verification failed.");
        }
        return await FinishLoginAsync(user);
    }

    // ======================== account settings ========================

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<MfaStatusDto>> Status()
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        var keys = await _db.UserPasskeys.Where(p => p.UserId == user.Id).OrderBy(p => p.CreatedAt)
            .Select(p => new PasskeyDto(p.Id, p.Name, p.CreatedAt, p.LastUsedAt)).ToListAsync();
        return new MfaStatusDto(_passkeys.Enabled, user.TotpEnabledAt != null, keys, await _mfa.RecoveryCodesLeftAsync(user.Id));
    }

    /// <summary>Start setting up an authenticator app: a fresh secret (not active until a code confirms it).</summary>
    [HttpPost("totp/setup")]
    [Authorize]
    public async Task<ActionResult<TotpSetupDto>> TotpSetup()
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (user.TotpEnabledAt != null) return Conflict("An authenticator app is already set up. Remove it first to set up a new one.");
        user.TotpSecret = MfaService.NewTotpSecret();
        user.TotpLastStep = 0;
        await _db.SaveChangesAsync();
        return new TotpSetupDto(user.TotpSecret, MfaService.OtpAuthUri(user.Email, user.TotpSecret));
    }

    /// <summary>Confirm the setup with a first code. Returns recovery codes when this is the account's first second factor.</summary>
    [HttpPost("totp/enable")]
    [Authorize]
    public async Task<ActionResult<MfaRecoveryCodesDto>> TotpEnable(MfaCodeDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (user.TotpEnabledAt != null) return Conflict("An authenticator app is already set up.");
        if (string.IsNullOrEmpty(user.TotpSecret)) return BadRequest("Start the setup again.");
        if (_throttle.IsBlocked(ClientIp, ThrottleKey(user.Id)))
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again in a few minutes.");
        if (!await _mfa.VerifyTotpAsync(user, dto.Code))
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey(user.Id));
            return BadRequest("That code is wrong. Check the time on your phone and try again.");
        }
        user.TotpEnabledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new MfaRecoveryCodesDto(await EnsureRecoveryCodesAsync(user));
    }

    [HttpPost("totp/disable")]
    [Authorize]
    public async Task<IActionResult> TotpDisable(MfaPasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (_pw.Reauth(user, dto.Password) is string pwErr) return BadRequest(pwErr);
        user.TotpSecret = null; user.TotpEnabledAt = null; user.TotpLastStep = 0;
        await _db.SaveChangesAsync();
        await DropRecoveryCodesIfNoFactorAsync(user);
        return NoContent();
    }

    /// <summary>Issue a fresh set of recovery codes (the old ones stop working).</summary>
    [HttpPost("recovery-codes")]
    [Authorize]
    public async Task<ActionResult<MfaRecoveryCodesDto>> RegenerateRecoveryCodes(MfaPasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (_pw.Reauth(user, dto.Password) is string pwErr) return BadRequest(pwErr);
        if (!await _mfa.HasMfaAsync(user)) return BadRequest("Set up an authenticator app or a passkey first.");
        return new MfaRecoveryCodesDto(await _mfa.RegenerateRecoveryCodesAsync(user));
    }

    // ---- passkeys ----

    [HttpPost("passkeys/options")]
    [Authorize]
    public async Task<IActionResult> PasskeyRegisterOptions()
    {
        if (!_passkeys.Enabled) return NotFound();
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        var existing = await _db.UserPasskeys.Where(p => p.UserId == user.Id).Select(p => p.CredentialId).ToListAsync();
        var options = _passkeys.CreationOptions(user, existing);
        return Ok(new { options, state = _mfa.ProtectState(StatePasskeyRegister, user.Id, options.ToJson()) });
    }

    public record PasskeyRegisterDto(string State, string? Name, AuthenticatorAttestationRawResponse Response);

    [HttpPost("passkeys")]
    [Authorize]
    public async Task<ActionResult<MfaRecoveryCodesDto>> PasskeyRegister(PasskeyRegisterDto dto)
    {
        if (!_passkeys.Enabled) return NotFound();
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();

        var json = _mfa.ReadState(StatePasskeyRegister, user.Id, dto.State);
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
        return new MfaRecoveryCodesDto(await EnsureRecoveryCodesAsync(user));
    }

    [HttpPatch("passkeys/{id:int}")]
    [Authorize]
    public async Task<IActionResult> RenamePasskey(int id, PasskeyNameDto dto)
    {
        var key = await _db.UserPasskeys.FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId);
        if (key is null) return NotFound();
        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest("Give the passkey a name.");
        key.Name = name.Length > 80 ? name[..80] : name;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("passkeys/{id:int}")]
    [Authorize]
    public async Task<IActionResult> RemovePasskey(int id, MfaPasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (_pw.Reauth(user, dto.Password) is string pwErr) return BadRequest(pwErr);
        var key = await _db.UserPasskeys.FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId);
        if (key is null) return NotFound();
        _db.UserPasskeys.Remove(key);
        await _db.SaveChangesAsync();
        await DropRecoveryCodesIfNoFactorAsync(user);
        return NoContent();
    }

    // ---- helpers ----

    /// <summary>Recovery codes appear when the first second factor is added (not on every later one).</summary>
    private async Task<List<string>?> EnsureRecoveryCodesAsync(User user) =>
        await _db.MfaRecoveryCodes.AnyAsync(c => c.UserId == user.Id) ? null : await _mfa.RegenerateRecoveryCodesAsync(user);

    private async Task DropRecoveryCodesIfNoFactorAsync(User user)
    {
        if (await _mfa.HasMfaAsync(user)) return;
        _db.MfaRecoveryCodes.RemoveRange(_db.MfaRecoveryCodes.Where(c => c.UserId == user.Id));
        await _db.SaveChangesAsync();
    }
}
