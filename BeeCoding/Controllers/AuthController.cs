using System.Security.Cryptography;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[Route("api/auth")]
public class AuthController(AppDbContext db, PasswordService pw, IConfiguration cfg, AdminAccess admin, LoginThrottle throttle,
    EmailService email, PasswordResetService resets, ResetRequestThrottle resetThrottle, EmailVerificationService verification,
    MfaService mfa, PasskeyService passkeys) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly PasswordService _pw = pw;
    private readonly IConfiguration _cfg = cfg;
    private readonly AdminAccess _admin = admin;
    private readonly LoginThrottle _throttle = throttle;
    private readonly EmailService _email = email;
    private readonly PasswordResetService _resets = resets;
    private readonly ResetRequestThrottle _resetThrottle = resetThrottle;
    private readonly EmailVerificationService _verification = verification;
    private readonly MfaService _mfa = mfa;
    private readonly PasskeyService _passkeys = passkeys;

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";

    private bool IsAdmin(string email) => _admin.IsAdminEmail(email);

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> Register(RegisterDto dto)
    {
        var email = (dto.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length < 3 || !email.Contains('@')) return BadRequest("Invalid email.");
        if (PasswordPolicy.Validate(dto.Password, email) is string pwErr) return BadRequest(pwErr);
        if (string.IsNullOrWhiteSpace(dto.DisplayName)) return BadRequest("Display name is required.");

        // Self-service registration only creates Students. A Teacher account requires the
        // shared invite code (Auth:TeacherSignupCode); when that config is unset, teacher
        // self-signup is disabled entirely (make teachers via the DB / an existing teacher).
        var wantsTeacher = dto.Role?.Equals("Teacher", StringComparison.OrdinalIgnoreCase) == true;
        var role = UserRole.Student;
        if (wantsTeacher)
        {
            var code = _cfg["Auth:TeacherSignupCode"];
            if (string.IsNullOrEmpty(code))
                return BadRequest("Teacher self-registration is disabled on this server.");
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(dto.TeacherCode ?? ""),
                    System.Text.Encoding.UTF8.GetBytes(code)))
                return BadRequest("Invalid teacher code.");
            role = UserRole.Teacher;
        }

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict("An account with that email already exists.");

        var user = new User
        {
            Email = email,
            DisplayName = dto.DisplayName.Trim(),
            Role = role,
        };
        user.PasswordHash = _pw.Hash(user, dto.Password!);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await SignInAsync(user);
        await _verification.SendAsync(user, Request);   // no-op unless outgoing email is configured
        return await MeDtoAsync(user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Login(LoginDto dto)
    {
        var email = (dto.Email ?? "").Trim().ToLowerInvariant();
        var ip = ClientIp;

        if (_throttle.IsBlocked(ip, email))
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again in a few minutes.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.DeletedAt is not null || !_pw.Verify(user, dto.Password ?? ""))
        {
            _throttle.RecordFailure(ip, email);
            return Unauthorized("Wrong email or password.");
        }

        _throttle.RecordSuccess(ip, email);

        // Right password but the account has a second factor: no session yet, just a ticket that
        // the /api/auth/mfa/* endpoints trade for one once the second factor checks out.
        if (await _mfa.HasMfaAsync(user))
        {
            var methods = new List<string>();
            if (user.TotpEnabledAt != null) methods.Add("totp");
            if (_passkeys.Enabled && await _db.UserPasskeys.AnyAsync(p => p.UserId == user.Id)) methods.Add("passkey");
            methods.Add("recovery");
            return new MfaChallengeDto(true, _mfa.IssueTicket(user.Id), methods.ToArray());
        }

        await SignInAsync(user);
        return await MeDtoAsync(user);
    }

    // Deliberately not [Authorize]: logging out must always succeed, even when the current
    // cookie state is already broken (e.g. an old pre-PathBase cookie at Path=/ conflicting
    // with the current one — ASP.NET Core can fail to parse either and treat the request as
    // unauthenticated, which would make an [Authorize]'d logout 401 instead of clearing
    // anything). Clearing cookies is safe and idempotent regardless of auth state.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await CookieSignIn.SignOutAsync(HttpContext);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeDto>> Me()
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        return await MeDtoAsync(user);
    }

    [HttpPatch("profile")]
    [Authorize]
    public async Task<ActionResult<MeDto>> UpdateProfile(UpdateProfileDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();

        var name = (dto.DisplayName ?? "").Trim();
        if (name.Length < 2) return BadRequest("Display name must be at least 2 characters.");
        if (name.Length > 40) return BadRequest("Display name must be 40 characters or fewer.");

        if (name != user.DisplayName)
        {
            user.DisplayName = name;
            await _db.SaveChangesAsync();
            await SignInAsync(user);   // refresh the cookie so ClaimTypes.Name (author names, etc.) is current
        }
        return await MeDtoAsync(user);
    }

    /// <summary>What the login page needs to know: is "forgot password" available (outgoing email configured)?</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public ActionResult<AuthConfigDto> Config() => new AuthConfigDto(_email.IsConfigured, _verification.Available, _verification.Required, _passkeys.Enabled);

    /// <summary>
    /// Email a reset link. Always answers 204 whether or not the address has an account (no
    /// account enumeration), and the send itself is not awaited so response time doesn't leak
    /// it either. Capped per address and per client IP.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        if (!_email.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Password reset by email isn't set up on this server. Ask an administrator.");

        var address = (dto.Email ?? "").Trim().ToLowerInvariant();
        if (address.Length is 0 or > 256 || !address.Contains('@')) return NoContent();
        if (!_resetThrottle.TryAcquire("ip:" + ClientIp, 20) || !_resetThrottle.TryAcquire("mail:" + address, 3)) return NoContent();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == address && u.DeletedAt == null);
        if (user is null) return NoContent();

        var (token, _) = await _resets.CreateAsync(user, PasswordResetService.EmailLifetime, provesEmail: true);
        var link = _resets.BuildLink(Request, token);
        var body =
            $"Hi {user.DisplayName},\n\n" +
            "Someone asked to reset the password for your BeeCoding account. Use this link to choose a new one " +
            $"(it works once and expires in {(int)PasswordResetService.EmailLifetime.TotalMinutes} minutes):\n\n{link}\n\n" +
            "If that wasn't you, ignore this email — your password stays as it is.\n";
        _ = _email.SendAsync(user.Email, "Reset your BeeCoding password", body);   // deliberately not awaited
        return NoContent();
    }

    /// <summary>Lets the reset page say "this link has expired" before the user types a password.</summary>
    [HttpGet("reset-password/check")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckResetToken([FromQuery] string? token) =>
        await _resets.FindValidAsync(token) is null ? BadRequest("This reset link is invalid or has expired.") : NoContent();

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        var row = await _resets.FindValidAsync(dto.Token);
        if (row?.User is not { } user) return BadRequest("This reset link is invalid or has expired.");
        if (PasswordPolicy.Validate(dto.NewPassword, user.Email) is string pwErr) return BadRequest(pwErr);

        user.PasswordHash = _pw.Hash(user, dto.NewPassword!);
        if (row.ProvesEmail) user.EmailVerifiedAt ??= DateTime.UtcNow;   // the link came to their mailbox
        await _db.SaveChangesAsync();
        await _resets.ConsumeAsync(row);
        _throttle.RecordSuccess(ClientIp, user.Email);   // a locked-out account can sign in again
        return NoContent();
    }

    /// <summary>Redeem an emailed verification link. Anonymous on purpose: the link is opened from
    /// the mailbox, often in a browser where the user isn't signed in.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(VerifyEmailDto dto) =>
        await _verification.RedeemAsync(dto.Token) is null ? BadRequest("This verification link is invalid or has expired.") : NoContent();

    /// <summary>Send the signed-in user a fresh verification link (capped per account).</summary>
    [HttpPost("resend-verification")]
    [Authorize]
    public async Task<IActionResult> ResendVerification()
    {
        if (!_verification.Available) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Email verification isn't set up on this server.");
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (user.EmailVerifiedAt is not null) return NoContent();
        if (!_resetThrottle.TryAcquire("verify:" + UserId, 3))
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many requests — check your inbox, or try again in an hour.");

        await _verification.SendAsync(user, Request);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();

        if (!_pw.Verify(user, dto.CurrentPassword ?? ""))
            return BadRequest("Current password is wrong.");
        if (PasswordPolicy.Validate(dto.NewPassword, user.Email) is string pwErr)
            return BadRequest(pwErr);

        user.PasswordHash = _pw.Hash(user, dto.NewPassword!);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Permanently delete the caller's own account and everything owned by it.</summary>
    [HttpDelete("account")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(DeleteAccountDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();
        if (!_pw.Verify(user, dto.Password ?? ""))
            return BadRequest("Password is wrong.");

        // Board.OwnerId is Restrict — a teacher's boards must go first (they carry other
        // people's submissions/posts, so require an explicit opt-in).
        var ownedBoards = await _db.Boards.Where(x => x.OwnerId == UserId)
            .Select(x => new { x.Id, x.Slug, x.Title }).ToListAsync();
        if (ownedBoards.Count > 0 && !dto.DeleteOwnedBoards)
            return Conflict(new
            {
                message = "You own boards. Deleting your account will also delete them (and everyone's work on them). Confirm to proceed.",
                boards = ownedBoards.Select(b => new { b.Slug, b.Title }),
            });

        if (ownedBoards.Count > 0)
            _db.Boards.RemoveRange(_db.Boards.Where(x => x.OwnerId == UserId));
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();   // cascades memberships, submissions, posts, bank problems, solve records

        await CookieSignIn.SignOutAsync(HttpContext);
        return NoContent();
    }

    // Admin is NOT baked into the cookie — it's evaluated fresh from Admin:Emails on every
    // request (see AdminAccess / the "Admin" authorization policy) so granting or revoking
    // it takes effect immediately, without a re-login. Shared with LtiController — see
    // CookieSignIn.
    private Task SignInAsync(User user) => CookieSignIn.SignInAsync(HttpContext, user);

    private Task<MeDto> MeDtoAsync(User user) => MeDtoBuilder.BuildAsync(_db, _admin, user);
}
