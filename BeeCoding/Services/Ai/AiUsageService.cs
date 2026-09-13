using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services.Ai;

public record AiUsageBucketDto(int Calls, long PromptTokens, long CompletionTokens, long TotalTokens);
public record AiUsageDto(
    AiUsageBucketDto Today, AiUsageBucketDto Month, AiUsageBucketDto AllTime,
    int DailyQuota, bool Blocked, string? BlockedReason);

/// <summary>Per-user, per-day rollup of AI token consumption.</summary>
public class AiUsageService(AppDbContext db, AiRuntimeSettings runtime)
{
    private readonly AppDbContext _db = db;
    private readonly AiRuntimeSettings _runtime = runtime;

    /// <summary>Ban / org-pause / daily-quota check for a user, before an AI call is allowed
    /// to proceed. <paramref name="organizationId"/> scopes the quota/pause to that
    /// organization's own AI settings when it has any (see AiRuntimeSettings.Effective) —
    /// pass null when the action has no board/org context (falls back to the platform
    /// default). Does NOT check the platform-wide pause on its own — that's folded into
    /// AiTutorService.Available, which every AI-invoking action already checks first — but
    /// checks it again here too since Effective is cheap and this stays correct standalone.</summary>
    public async Task<(bool Allowed, string? Reason)> CheckGateAsync(int userId, string role, int? organizationId, CancellationToken ct = default)
    {
        var (blocked, quota, reason) = _runtime.Effective(userId, role, organizationId);
        if (blocked) return (false, reason);
        if (quota <= 0) return (false, "Your AI access has been disabled by an admin.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var used = await _db.AiUsages.Where(x => x.UserId == userId && x.Day == today)
            .Select(x => x.Calls).FirstOrDefaultAsync(ct);
        return used >= quota
            ? (false, $"You've reached today's AI limit ({quota} requests). Try again tomorrow.")
            : (true, null);
    }

    public async Task RecordAsync(int userId, int promptTokens, int completionTokens, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await _db.AiUsages.FirstOrDefaultAsync(x => x.UserId == userId && x.Day == today, ct);
        if (row is null)
        {
            row = new AiUsage { UserId = userId, Day = today };
            _db.AiUsages.Add(row);
        }
        row.Calls += 1;
        row.PromptTokens += Math.Max(0, promptTokens);
        row.CompletionTokens += Math.Max(0, completionTokens);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // lost the race to create today's row — merge into the existing one.
            _db.ChangeTracker.Clear();
            var existing = await _db.AiUsages.FirstAsync(x => x.UserId == userId && x.Day == today, ct);
            existing.Calls += 1;
            existing.PromptTokens += Math.Max(0, promptTokens);
            existing.CompletionTokens += Math.Max(0, completionTokens);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<AiUsageDto> SummaryAsync(int userId, string role, int? organizationId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var rows = await _db.AiUsages.Where(x => x.UserId == userId).ToListAsync(ct);

        static AiUsageBucketDto Sum(IEnumerable<AiUsage> xs)
        {
            int c = 0; long p = 0, k = 0;
            foreach (var x in xs) { c += x.Calls; p += x.PromptTokens; k += x.CompletionTokens; }
            return new AiUsageBucketDto(c, p, k, p + k);
        }

        var (blocked, quota, reason) = _runtime.Effective(userId, role, organizationId);
        return new AiUsageDto(
            Sum(rows.Where(x => x.Day == today)),
            Sum(rows.Where(x => x.Day >= monthStart)),
            Sum(rows),
            quota, blocked, reason);
    }
}
