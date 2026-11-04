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
}
