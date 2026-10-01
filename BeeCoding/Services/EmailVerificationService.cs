using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Sends and redeems "confirm your email address" links. Verification only does anything when
/// outgoing email is configured; <c>Auth:RequireVerifiedEmail</c> additionally locks unverified
/// accounts out of the app (see <see cref="EmailVerificationGate"/>).
/// </summary>
public class EmailVerificationService(AppDbContext db, EmailService email, IConfiguration cfg)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private readonly AppDbContext _db = db;
    private readonly EmailService _email = email;
    private readonly IConfiguration _cfg = cfg;

    public bool Available => _email.IsConfigured;
    /// <summary>The hard gate is only ever on when mail can actually be delivered, so a
    /// misconfigured server can't lock everyone out.</summary>
    public bool Required => _email.IsConfigured && _cfg.GetValue<bool>("Auth:RequireVerifiedEmail");

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Create a link for the user's current address and email it (not awaited — a slow
    /// relay must not slow the request). Returns false when email isn't configured.</summary>
    public async Task<bool> SendAsync(User user, HttpRequest request)
    {
        if (!_email.IsConfigured || user.EmailVerifiedAt is not null) return false;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTime.UtcNow;
        await _db.EmailVerificationTokens
            .Where(t => t.UserId == user.Id && (t.UsedAt != null || t.ExpiresAt < now))
            .ExecuteDeleteAsync();
        _db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            UserId = user.Id, Email = user.Email, TokenHash = Hash(token), CreatedAt = now, ExpiresAt = now + Lifetime,
        });
        await _db.SaveChangesAsync();

        var link = PublicUrl.Build(_cfg, request, $"/verify-email?token={Uri.EscapeDataString(token)}");
        var body =
            $"Hi {user.DisplayName},\n\n" +
            "Please confirm your email address for your BeeCoding account by opening this link " +
            $"(it works once and expires in {(int)Lifetime.TotalHours} hours):\n\n{link}\n\n" +
            "If you didn't create a BeeCoding account, you can ignore this email.\n";
        _ = _email.SendAsync(user.Email, "Confirm your BeeCoding email", body);   // deliberately not awaited
        return true;
    }

    /// <summary>Redeem a link: mark the user's email verified. Null if the link is unknown, used,
    /// expired, or the account's address changed since it was sent.</summary>
    public async Task<User?> RedeemAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return null;
        var hash = Hash(token.Trim());
        var now = DateTime.UtcNow;
        var row = await _db.EmailVerificationTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now);
        var user = row?.User;
        if (row is null || user is null || user.DeletedAt is not null
            || !string.Equals(user.Email, row.Email, StringComparison.OrdinalIgnoreCase)) return null;

        user.EmailVerifiedAt ??= now;
        await _db.EmailVerificationTokens.Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
        await _db.SaveChangesAsync();
        return user;
    }
}

/// <summary>
/// Hard gate behind <c>Auth:RequireVerifiedEmail</c>: while on (and email is configured), a signed-in
/// user whose email isn't verified can only reach <c>/api/auth/*</c> (to read their profile, resend
/// the link, sign out). Platform admins are exempt so they can't lock themselves out.
/// </summary>
public sealed class EmailVerificationGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, EmailVerificationService verification, AppDbContext db, AdminAccess admin)
    {
        var path = ctx.Request.Path;
        bool guarded = (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
                       && !path.StartsWithSegments("/api/auth");
        if (guarded && verification.Required && ctx.User.Identity?.IsAuthenticated == true
            && int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)
            && !admin.IsAdminEmail(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value))
        {
            var verified = await db.Users.Where(u => u.Id == userId).Select(u => u.EmailVerifiedAt != null).FirstOrDefaultAsync();
            if (!verified)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { message = "Verify your email to continue.", code = "email_unverified" });
                return;
            }
        }
        await next(ctx);
    }
}
