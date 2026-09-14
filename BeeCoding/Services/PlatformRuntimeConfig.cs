namespace BeeCoding.Services;

/// <summary>
/// In-memory cache of the judge/LSP runtime overrides (see PlatformRuntimeSettings) —
/// populated from the DB at startup and kept in sync by AdminUiController's write, so the
/// hot paths (every LSP connection attempt, every submit/run) never need a DB round trip.
///
/// Caveat: per-instance cache, same as AdminAccess/AiRuntimeSettings — in a scale-out
/// deployment a change on one instance isn't visible on the others until they restart.
/// </summary>
public class PlatformRuntimeConfig
{
    public bool LspEnabled { get; private set; }
    public int JudgeRateLimitMs { get; private set; } = 1500;

    public void Set(bool lspEnabled, int judgeRateLimitMs)
    {
        LspEnabled = lspEnabled;
        JudgeRateLimitMs = Math.Max(0, judgeRateLimitMs);
    }
}
