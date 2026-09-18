using System.Collections.Concurrent;

namespace BeeCoding.Services;

/// <summary>
/// Minimum spacing specifically between two SUBMIT actions (board or practice) by the same
/// user — separate from RateLimiter's general run/submit spacing (1.5s, which stays snappy
/// for iterating with Run). Submitting again right after a submit is worth slowing down
/// harder, since each one queues a full compile + test run.
/// </summary>
public class SubmitCooldown
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<int, DateTime> _last = new();

    /// <summary>Seconds still remaining before the next submit is allowed (0 if allowed now).</summary>
    public double SecondsRemaining(int userId)
    {
        if (_last.TryGetValue(userId, out var last))
        {
            var elapsed = DateTime.UtcNow - last;
            if (elapsed < Window) return (Window - elapsed).TotalSeconds;
        }
        return 0;
    }

    /// <summary>Returns true if allowed; records the timestamp when allowed.</summary>
    public bool TryAcquire(int userId)
    {
        if (SecondsRemaining(userId) > 0) return false;
        _last[userId] = DateTime.UtcNow;
        return true;
    }
}
