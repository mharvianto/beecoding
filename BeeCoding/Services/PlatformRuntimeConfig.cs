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

    /// <summary>Students share one clangd per language instead of one process each (see SharedClangd).</summary>
    public bool LspShared { get; private set; }
    public void SetLspShared(bool shared) => LspShared = shared;
    public int JudgeRateLimitMs { get; private set; } = 1500;

    public bool MfaRequireAdmin { get; private set; }
    public bool MfaRequireOrgAdmin { get; private set; }
    public bool MfaRequireTeacher { get; private set; }

    public int StudentMaxProblems { get; private set; } = 50;

    public void SetStudentMaxProblems(int max) => StudentMaxProblems = Math.Max(0, max);

    public int ImageMaxKb { get; private set; } = 1024;
    public int ImageMaxPerUser { get; private set; } = 100;
    public int NotificationReadRetentionDays { get; private set; } = 7;
    public void SetNotificationReadRetentionDays(int days) => NotificationReadRetentionDays = Math.Max(0, days);

    /// <summary>Kinds off for everyone, and kinds that may be emailed (null in the database = NotificationKinds.DefaultEmail).</summary>
    public HashSet<string> NotifyOffKinds { get; private set; } = [];
    public HashSet<string> NotifyEmailKinds { get; private set; } = [.. NotificationKinds.DefaultEmail];
    public HashSet<string> NotifyLockedKinds { get; private set; } = [];
    public void SetNotifyKinds(string? off, string? email, string? locked)
    {
        NotifyLockedKinds = NotificationKinds.Parse(locked);
        NotifyOffKinds = NotificationKinds.Parse(off);
        NotifyEmailKinds = email is null ? [.. NotificationKinds.DefaultEmail] : NotificationKinds.Parse(email);
    }

    public void SetImageLimits(int maxKb, int maxPerUser) { ImageMaxKb = Math.Max(1, maxKb); ImageMaxPerUser = Math.Max(0, maxPerUser); }

    public void SetMfaPolicy(bool admin, bool orgAdmin, bool teacher)
    {
        MfaRequireAdmin = admin; MfaRequireOrgAdmin = orgAdmin; MfaRequireTeacher = teacher;
    }

    public void Set(bool lspEnabled, int judgeRateLimitMs)
    {
        LspEnabled = lspEnabled;
        JudgeRateLimitMs = Math.Max(0, judgeRateLimitMs);
    }
}
