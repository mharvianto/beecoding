namespace BeeCoding.Services.Judge;

public class JudgeOptions
{
    /// <summary>Scratch directory for compiles + sandboxed runs.</summary>
    public string WorkRoot { get; set; } = Path.Combine(Path.GetTempPath(), "beecoding-judge");

    /// <summary>Parallel compile/run slots.</summary>
    public int MaxConcurrent { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int CompileTimeoutMs { get; set; } = 10_000;

    public int QueueCapacity { get; set; } = 200;

    /// <summary>
    /// Fail startup if bubblewrap can't create namespaces. Set true in production so the
    /// judge never silently degrades to "rlimits only" (no filesystem/network isolation —
    /// student code could read the app's files or open outbound sockets).
    /// </summary>
    public bool RequireSandbox { get; set; }

    /// <summary>Extra wall-clock grace on top of a problem's time limit before the parent force-kills.</summary>
    public int HardWallBufferMs { get; set; } = 800;

    /// <summary>Max stdout/stderr bytes captured per run (rest is truncated, so a longer correct answer reads as Wrong Answer).
    /// 4 MB: large problems legitimately print a few hundred thousand numbers; a runaway print loop is still cut off.</summary>
    public int MaxOutputBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Ad-hoc "Run" defaults when no problem context is supplied.</summary>
    public int RunTimeLimitMs { get; set; } = 1000;
    public int RunMemoryLimitKb { get; set; } = 32_768;

    /// <summary>Per-user minimum spacing between run/submit requests.</summary>
    public int RateLimitMs { get; set; } = 1500;

    /// <summary>Where judge jobs travel (see DEPLOY.md §2.6).</summary>
    public JudgeQueueOptions Queue { get; set; } = new();
}

public sealed class JudgeQueueOptions
{
    /// <summary>"inproc" (default, single node) | "redis" (jobs on a broker; judge can be a
    /// separate deployment).</summary>
    public string Backend { get; set; } = "inproc";

    /// <summary>StackExchange.Redis connection string. If null, reuses Realtime's multiplexer.</summary>
    public string? RedisConnectionString { get; set; }

    public string KeyPrefix { get; set; } = "bc:judge:";

    /// <summary>How long a producer waits for an ad-hoc Run's result before giving up.</summary>
    public int RunReplyTimeoutSeconds { get; set; } = 60;

    /// <summary>Poll interval when the job list is empty (redis backend).</summary>
    public int PollMs { get; set; } = 200;

    public bool UseRedis => string.Equals(Backend, "redis", StringComparison.OrdinalIgnoreCase);
}
