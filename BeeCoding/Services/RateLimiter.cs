using BeeCoding.Services.Realtime;

namespace BeeCoding.Services;

/// <summary>Minimum spacing between a user's run/submit requests — the window itself is
/// live-tunable from /admin/reports (see PlatformRuntimeConfig), not fixed at startup.
/// Kept in an <see cref="ICounterStore"/>, so with Redis the spacing holds across web instances.</summary>
public class RateLimiter(PlatformRuntimeConfig cfg, ICounterStore? store = null)
{
    private readonly ICounterStore _store = store ?? new InMemoryCounterStore();

    /// <summary>Returns true if allowed; records the timestamp when allowed. Check and write are one atomic step (SET NX).</summary>
    public bool TryAcquire(int userId)
    {
        var ms = cfg.JudgeRateLimitMs;
        return ms <= 0 || _store.TryClaim("rl:" + userId, TimeSpan.FromMilliseconds(ms));
    }
}
