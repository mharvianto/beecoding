using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Issues and redeems single-use password-reset tokens. Only a SHA-256 of each token is stored,
/// so a database leak doesn't hand out working reset links.
/// </summary>
public class PasswordResetService(AppDbContext db, IConfiguration cfg)
{
    public static readonly TimeSpan EmailLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan AdminLifetime = TimeSpan.FromHours(24);

    private readonly AppDbContext _db = db;
    private readonly IConfiguration _cfg = cfg;

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Create a token for <paramref name="user"/> and return the raw value (shown once).</summary>
    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(User user, TimeSpan lifetime)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        // Housekeeping: this user's spent/expired tokens are of no further use.
        await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && (t.UsedAt != null || t.ExpiresAt < now))
            .ExecuteDeleteAsync();
        var row = new PasswordResetToken { UserId = user.Id, TokenHash = Hash(token), CreatedAt = now, ExpiresAt = now + lifetime };
        _db.PasswordResetTokens.Add(row);
        await _db.SaveChangesAsync();
        return (token, row.ExpiresAt);
    }

    /// <summary>The unused, unexpired token row (with its user), or null.</summary>
    public async Task<PasswordResetToken?> FindValidAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return null;
        var hash = Hash(token.Trim());
        var now = DateTime.UtcNow;
        var row = await _db.PasswordResetTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now);
        return row?.User is { DeletedAt: null } ? row : null;
    }

    /// <summary>How many reset tokens this user was issued in the last hour (for a per-account cap).</summary>
    public Task<int> RecentCountAsync(int userId) =>
        _db.PasswordResetTokens.CountAsync(t => t.UserId == userId && t.CreatedAt > DateTime.UtcNow.AddHours(-1));

    /// <summary>Mark the token used and void every other outstanding one for the user.</summary>
    public async Task ConsumeAsync(PasswordResetToken row)
    {
        var now = DateTime.UtcNow;
        await _db.PasswordResetTokens
            .Where(t => t.UserId == row.UserId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
    }

    /// <summary>Absolute URL of the reset page. Prefers config <c>App:PublicUrl</c> (so a forged
    /// Host header can't redirect the link to another site); falls back to the request.</summary>
    public string BuildLink(HttpRequest request, string token)
    {
        var configured = _cfg["App:PublicUrl"]?.Trim().TrimEnd('/');
        var baseUrl = string.IsNullOrEmpty(configured)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : configured;
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
    }

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Fixed-window caps on "forgot password" requests (per IP and per address) so the
/// form can't be used to spam someone's inbox or to hammer the mail relay.</summary>
public sealed class ResetRequestThrottle
{
    private static readonly long Window = TimeSpan.FromHours(1).Ticks;
    private readonly ConcurrentDictionary<string, (int Count, long Start)> _hits = new();

    /// <summary>True (and counts the hit) while <paramref name="key"/> is under <paramref name="cap"/> for the window.</summary>
    public bool TryAcquire(string key, int cap)
    {
        var now = DateTime.UtcNow.Ticks;
        var allowed = false;
        _hits.AddOrUpdate(key,
            _ => { allowed = true; return (1, now); },
            (_, cur) =>
            {
                if (now - cur.Start > Window) { allowed = true; return (1, now); }
                if (cur.Count >= cap) return cur;
                allowed = true;
                return (cur.Count + 1, cur.Start);
            });
        if (_hits.Count > 5000)
            foreach (var kv in _hits.Where(kv => now - kv.Value.Start > Window).ToList()) _hits.TryRemove(kv.Key, out _);
        return allowed;
    }
}
