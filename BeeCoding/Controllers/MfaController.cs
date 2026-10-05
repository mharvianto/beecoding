using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Second-factor sign-in (authenticator app, recovery code) and the account settings that
/// manage them. The anonymous endpoints take the ticket that <c>POST /api/auth/login</c> hands out
/// after a correct password; the authenticated ones need a session.
/// </summary>
[Route("api/auth/mfa")]
public class MfaController(AppDbContext db, PasswordService pw, MfaService mfa,
    LoginThrottle throttle, AdminAccess admin) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly PasswordService _pw = pw;
    private readonly MfaService _mfa = mfa;
    private readonly LoginThrottle _throttle = throttle;
    private readonly AdminAccess _admin = admin;

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
    private static string ThrottleKey(int userId) => $"mfa:{userId}";

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
            "email" => user.EmailMfaEnabledAt != null && await _mfa.VerifyEmailCodeAsync(user, dto.Code),
            "recovery" => await _mfa.UseRecoveryCodeAsync(user.Id, dto.Code),
            _ => false,
        };
        if (!ok)
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey(user.Id));
            return Unauthorized(dto.Method switch
            {
                "recovery" => "That recovery code is wrong or already used.",
                "email" => "Wrong or expired code. Ask for a new one and try again.",
                _ => "Wrong code. Check your authenticator app and try again.",
            });
        }
        return await FinishLoginAsync(user);
    }

    /// <summary>Which second-factor methods an in-progress login can finish with (used when Google sign-in
    /// hands the browser a ticket by redirect instead of by JSON).</summary>
    [HttpPost("methods")]
    [AllowAnonymous]
    public async Task<ActionResult<MfaMethodsDto>> Methods(MfaTicketDto dto)
    {
        var (user, fail) = await LoadForTicketAsync(dto.Ticket);
        if (user is null) return fail!;
        var methods = _mfa.Methods(user);
        return new MfaMethodsDto(methods, methods.Contains("email") ? MfaService.MaskEmail(user.Email) : null);
    }

    /// <summary>Email the user a sign-in code (they chose "email me a code" on the second-step screen).</summary>
    [HttpPost("email/send")]
    [AllowAnonymous]
    public async Task<IActionResult> SendSignInEmailCode(MfaTicketDto dto)
    {
        var (user, fail) = await LoadForTicketAsync(dto.Ticket);
        if (user is null) return fail!;
        if (user.EmailMfaEnabledAt == null) return BadRequest("Email codes aren't turned on for this account.");
        var (error, status) = await _mfa.SendEmailCodeAsync(user, signIn: true);
        return error is null ? NoContent() : StatusCode(status, error);
    }

    // ======================== account settings ========================

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<MfaStatusDto>> Status()
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        return new MfaStatusDto(user.TotpEnabledAt != null, user.EmailMfaEnabledAt != null, _mfa.EmailAvailable, await _mfa.RecoveryCodesLeftAsync(user.Id));
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

    // ---- email code as a second factor ----

    /// <summary>Start turning on email codes: sends a code to the account's address to prove it works.</summary>
    [HttpPost("email/setup")]
    [Authorize]
    public async Task<IActionResult> EmailSetup()
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (user.EmailMfaEnabledAt != null) return Conflict("Email codes are already turned on.");
        var (error, status) = await _mfa.SendEmailCodeAsync(user, signIn: false);
        return error is null ? NoContent() : StatusCode(status, error);
    }

    [HttpPost("email/enable")]
    [Authorize]
    public async Task<ActionResult<MfaRecoveryCodesDto>> EmailEnable(MfaCodeDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (user.EmailMfaEnabledAt != null) return Conflict("Email codes are already turned on.");
        if (_throttle.IsBlocked(ClientIp, ThrottleKey(user.Id)))
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again in a few minutes.");
        if (!await _mfa.VerifyEmailCodeAsync(user, dto.Code))
        {
            _throttle.RecordFailure(ClientIp, ThrottleKey(user.Id));
            return BadRequest("That code is wrong or expired. Send a new one and try again.");
        }
        user.EmailMfaEnabledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new MfaRecoveryCodesDto(await EnsureRecoveryCodesAsync(user));
    }

    [HttpPost("email/disable")]
    [Authorize]
    public async Task<IActionResult> EmailDisable(MfaPasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (_pw.Reauth(user, dto.Password) is string pwErr) return BadRequest(pwErr);
        user.EmailMfaEnabledAt = null;
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
        if (!await _mfa.HasMfaAsync(user)) return BadRequest("Turn on an authenticator app or email codes first.");
        return new MfaRecoveryCodesDto(await _mfa.RegenerateRecoveryCodesAsync(user));
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
