using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Second-factor sign-in: authenticator-app codes (TOTP, RFC 6238), single-use recovery codes, and the
/// short-lived "password was right, second factor pending" ticket that links the two login steps.
/// Passkeys live in <see cref="PasskeyService"/>.
/// </summary>
public class MfaService(AppDbContext db, IDataProtectionProvider dp)
{
    public const string Issuer = "BeeCoding";
    public const int RecoveryCodeCount = 10;
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db = db;
    private readonly ITimeLimitedDataProtector _tickets = dp.CreateProtector("beecoding.mfa-ticket").ToTimeLimitedDataProtector();

    public async Task<bool> HasMfaAsync(User user) =>
        user.TotpEnabledAt != null || await _db.UserPasskeys.AnyAsync(p => p.UserId == user.Id);

    // ---- login ticket ------------------------------------------------------

    /// <summary>Issued after a correct password when the account has a second factor. It proves only
    /// step one — it is not a session — and expires quickly.</summary>
    public string IssueTicket(int userId) => _tickets.Protect(userId.ToString(), TicketLifetime);

    public int? ReadTicket(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        try { return int.TryParse(_tickets.Unprotect(ticket), out var id) ? id : null; }
        catch (CryptographicException) { return null; }   // forged, tampered or expired
    }

    /// <summary>Opaque state carried by the browser between a WebAuthn "options" call and its "verify"
    /// call (holds the challenge), so no server-side session is needed. Bound to a purpose + user.</summary>
    public string ProtectState(string purpose, int userId, string json) =>
        _tickets.Protect($"{purpose}|{userId}|{json}", TicketLifetime);

    public string? ReadState(string purpose, int userId, string? state)
    {
        if (string.IsNullOrEmpty(state)) return null;
        try
        {
            var s = _tickets.Unprotect(state);
            var prefix = $"{purpose}|{userId}|";
            return s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : null;
        }
        catch (CryptographicException) { return null; }
    }

    // ---- TOTP --------------------------------------------------------------

    public static string NewTotpSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string OtpAuthUri(string email, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

    /// <summary>Checks a 6-digit code against the user's secret, allowing one step of clock drift either
    /// way, and refuses a step that was already used. On success the step is recorded.</summary>
    public async Task<bool> VerifyTotpAsync(User user, string? code, bool saveStep = true)
    {
        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(user.TotpSecret) || digits.Length != 6) return false;
        var key = Base32Decode(user.TotpSecret);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (long step = now - 1; step <= now + 1; step++)
        {
            if (step <= user.TotpLastStep) continue;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hotp(key, step)), Encoding.ASCII.GetBytes(digits))) continue;
            if (saveStep) { user.TotpLastStep = step; await _db.SaveChangesAsync(); }
            return true;
        }
        return false;
    }

    private static string Hotp(byte[] key, long counter)
    {
        var msg = new byte[8];
        for (int i = 7; i >= 0; i--) { msg[i] = (byte)(counter & 0xff); counter >>= 8; }
        var h = HMACSHA1.HashData(key, msg);
        int o = h[^1] & 0x0f;
        int bin = ((h[o] & 0x7f) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    private const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(B32[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(B32[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string s)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s.ToUpperInvariant())
        {
            int v = B32.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return bytes.ToArray();
    }

    // ---- recovery codes ----------------------------------------------------

    private const string CodeAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";   // no look-alikes (i l o 0 1)

    /// <summary>Replaces the user's recovery codes with a fresh set and returns the plain codes
    /// (xxxxx-xxxxx) — the only time they are ever visible.</summary>
    public async Task<List<string>> RegenerateRecoveryCodesAsync(User user)
    {
        _db.MfaRecoveryCodes.RemoveRange(_db.MfaRecoveryCodes.Where(c => c.UserId == user.Id));
        var codes = new List<string>();
        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            var raw = new string(Enumerable.Range(0, 10).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
            codes.Add($"{raw[..5]}-{raw[5..]}");
            _db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = user.Id, CodeHash = HashCode(raw) });
        }
        await _db.SaveChangesAsync();
        return codes;
    }

    public Task<int> RecoveryCodesLeftAsync(int userId) =>
        _db.MfaRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null);

    /// <summary>Burns a recovery code if it is valid and unused.</summary>
    public async Task<bool> UseRecoveryCodeAsync(int userId, string? code)
    {
        var raw = new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (raw.Length != 10) return false;
        var hash = HashCode(raw);
        var row = await _db.MfaRecoveryCodes.FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && c.UsedAt == null);
        if (row is null) return false;
        row.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    private static string HashCode(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Removes every second factor and recovery code (admin reset / last factor removed).</summary>
    public async Task ClearAllAsync(User user)
    {
        user.TotpSecret = null; user.TotpEnabledAt = null; user.TotpLastStep = 0;
        _db.UserPasskeys.RemoveRange(_db.UserPasskeys.Where(p => p.UserId == user.Id));
        _db.MfaRecoveryCodes.RemoveRange(_db.MfaRecoveryCodes.Where(c => c.UserId == user.Id));
        await _db.SaveChangesAsync();
    }
}
