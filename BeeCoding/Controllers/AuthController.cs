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
public class AuthController(AppDbContext db, PasswordService pw, IConfiguration cfg, AdminAccess admin, LoginThrottle throttle) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly PasswordService _pw = pw;
    private readonly IConfiguration _cfg = cfg;
    private readonly AdminAccess _admin = admin;
    private readonly LoginThrottle _throttle = throttle;

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";

    private bool IsAdmin(string email) => _admin.IsAdminEmail(email);

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> Register(RegisterDto dto)
    {
        var email = (dto.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length < 3 || !email.Contains('@')) return BadRequest("Invalid email.");
        if ((dto.Password ?? "").Length < 6) return BadRequest("Password must be at least 6 characters.");
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
        return await MeDtoAsync(user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<MeDto>> Login(LoginDto dto)
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
        await SignInAsync(user);
        return await MeDtoAsync(user);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();

        if (!_pw.Verify(user, dto.CurrentPassword ?? ""))
            return BadRequest("Current password is wrong.");
        if ((dto.NewPassword ?? "").Length < 6)
            return BadRequest("New password must be at least 6 characters.");

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

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    // Admin is NOT baked into the cookie — it's evaluated fresh from Admin:Emails on every
    // request (see AdminAccess / the "Admin" authorization policy) so granting or revoking
    // it takes effect immediately, without a re-login. Shared with LtiController — see
    // CookieSignIn.
    private Task SignInAsync(User user) => CookieSignIn.SignInAsync(HttpContext, user);

    private async Task<MeDto> MeDtoAsync(User user)
    {
        var isAdmin = IsAdmin(user.Email);
        // A platform super admin can manage every organization (see OrgAccess.CanManageAsync),
        // so they should see the Organization nav link too, not just members with OrgRole.Admin.
        var hasOrgAdmin = isAdmin || await _db.OrganizationMemberships.AnyAsync(m => m.UserId == user.Id && m.Role == OrgRole.Admin);
        return new MeDto(user.Id, user.Email, user.DisplayName, user.Role.ToString(), isAdmin, hasOrgAdmin);
    }
}
