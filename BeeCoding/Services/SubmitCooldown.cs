using BeeCoding.Services.Realtime;

namespace BeeCoding.Services;

/// <summary>
/// Minimum spacing specifically between two SUBMIT actions (board or practice) by the same
/// user — separate from RateLimiter's general run/submit spacing (1.5s, which stays snappy
/// for iterating with Run). Submitting again right after a submit is worth slowing down
/// harder, since each one queues a full compile + test run. Shared across instances with Redis.
/// </summary>
public class SubmitCooldown(ICounterStore? store = null)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    private readonly ICounterStore _store = store ?? new InMemoryCounterStore();

    /// <summary>Seconds still remaining before the next submit is allowed (0 if allowed now).</summary>
    public double SecondsRemaining(int userId) => _store.Remaining("sc:" + userId)?.TotalSeconds ?? 0;

    /// <summary>Returns true if allowed; records the timestamp when allowed (one atomic step: parallel submits cannot all pass).</summary>
    public bool TryAcquire(int userId) => _store.TryClaim("sc:" + userId, Window);
}
