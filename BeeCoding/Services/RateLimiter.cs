using System.Collections.Concurrent;

namespace BeeCoding.Services;

/// <summary>Minimum spacing between a user's run/submit requests — the window itself is
/// live-tunable from /admin/reports (see PlatformRuntimeConfig), not fixed at startup.</summary>
public class RateLimiter(PlatformRuntimeConfig cfg)
{
    private readonly ConcurrentDictionary<int, long> _last = new();
    private readonly PlatformRuntimeConfig _cfg = cfg;

    /// <summary>Returns true if allowed; records the timestamp when allowed.</summary>
    public bool TryAcquire(int userId)
    {
        var minTicks = TimeSpan.FromMilliseconds(_cfg.JudgeRateLimitMs).Ticks;
        var now = DateTime.UtcNow.Ticks;
        var prev = _last.GetOrAdd(userId, 0);
        if (now - prev < minTicks) return false;
        _last[userId] = now;
        return true;
    }
}
