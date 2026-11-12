using System.Security.Cryptography;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services.Ai;

/// <summary>
/// Tracks how many times a user has asked the AI tutor for a hint on the same problem
/// recently, so the tutor can escalate detail. Escalates only when the student asks again
/// WITHOUT changing their code; a real attempt keeps the level where it is, and a quiet
/// gap resets it.
/// </summary>
public class AiHintProgressService(AppDbContext db)
{
    private readonly AppDbContext _db = db;
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(90);
    public const int MaxLevel = 4;

    public static string HashCode(string? code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code ?? ""));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>Bump the counter for (user, problem) and return the current hint level (1..MaxLevel).</summary>
    public async Task<int> BumpAsync(int userId, string problemKey, string codeHash, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = await _db.AiHintProgresses
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ProblemKey == problemKey, ct);
            var now = DateTime.UtcNow;

            if (row is null)
            {
                row = new AiHintProgress { UserId = userId, ProblemKey = problemKey, Count = 0 };
                _db.AiHintProgresses.Add(row);
            }
            else if (now - row.UpdatedAt > ResetAfter)
            {
                row.Count = 0;                                   // been a while — start gently again
            }
            else if (!string.IsNullOrEmpty(row.LastCodeHash) && row.LastCodeHash != codeHash)
            {
                row.Count = Math.Max(0, row.Count - 2);          // they tried something — ease back a step
            }

            row.Count += 1;
            row.LastCodeHash = codeHash;
            row.UpdatedAt = now;

            try
            {
                await _db.SaveChangesAsync(ct);
                return Math.Clamp(row.Count, 1, MaxLevel);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                _db.ChangeTracker.Clear();                       // lost a create race — retry once
            }
        }
    }

    public async Task ResetAsync(int userId, string problemKey, CancellationToken ct = default)
    {
        var row = await _db.AiHintProgresses
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ProblemKey == problemKey, ct);
        if (row is null) return;
        _db.AiHintProgresses.Remove(row);
        await _db.SaveChangesAsync(ct);
    }
}
