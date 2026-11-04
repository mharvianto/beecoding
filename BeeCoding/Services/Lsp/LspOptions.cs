namespace BeeCoding.Services.Lsp;

public class LspOptions
{
    /// <summary>Enable the C/C++ language server bridge (needs <c>clangd</c> on the host).</summary>
    public bool Enabled { get; set; }

    public string ClangdPath { get; set; } = "clangd";

    /// <summary>Max concurrent clangd sessions.</summary>
    public int MaxConcurrent { get; set; } = 4;

    /// <summary>Kill a session after this long with no traffic.</summary>
    public int IdleTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Optional RLIMIT_DATA ceiling per clangd (MB, via <c>prlimit</c>). Acts only as a
    /// runaway guard — set it generously (clangd needs ~1&#8211;2&#160;GB to parse libstdc++
    /// headers). 0 disables it. A too-low value makes clangd abort on std header indexing.
    /// </summary>
    public int MemoryLimitMb { get; set; } = 0;

    // ---- shared mode (Admin > System: "Share one clangd between students") ----
    /// <summary>In shared mode, how many students one clangd process serves at once (per language).</summary>
    public int SharedMaxSessions { get; set; } = 40;

    /// <summary>clangd -j for the shared process: how many files it can parse at the same time.</summary>
    public int SharedJobs { get; set; } = 4;

    /// <summary>RLIMIT_DATA for the shared process (MB, 0 = none). It holds many files, so size it for the whole class.</summary>
    public int SharedMemoryLimitMb { get; set; } = 0;

    /// <summary>"memory" or "disk": where the shared clangd keeps each open file's preamble (disk trades speed for RAM).</summary>
    public string SharedPchStorage { get; set; } = "memory";

    /// <summary>The shared process exits this long after its last student left, so it does not sit on memory between classes.</summary>
    public int SharedIdleStopSeconds { get; set; } = 120;
}
