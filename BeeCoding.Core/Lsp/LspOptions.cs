namespace BeeCoding.Services.Lsp;

public class LspOptions
{
    /// <summary>Enable the C/C++ language server bridge (needs <c>clangd</c> on the host).</summary>
    public bool Enabled { get; set; }

    public string ClangdPath { get; set; } = "clangd";

    /// <summary>Run each clangd inside bubblewrap (no network, read-only /usr, only its own workspace writable), like the judge. Off by default
    /// so an existing install keeps working; turn it on where bwrap works (<c>Judge:RequireSandbox</c> hosts). Without it clangd runs as the app's
    /// OS user and a <c>#include "/some/file"</c> in a student's code can make it read any file that user can read. With it on and no
    /// <c>/usr/bin/bwrap</c>, the language server refuses to start instead of running unjailed. clangd must live under /usr.</summary>
    public bool Sandbox { get; set; }

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

    /// <summary>In shared mode, how many clangd processes per language may run. A new one starts only when the ones already
    /// running are full (<see cref="SharedMaxSessions"/> each). More processes spread the parsing over more CPU cores and
    /// keep one stuck clangd from taking every student down; they do NOT reduce memory, which grows with open files.</summary>
    public int SharedMaxProcesses { get; set; } = 1;

    /// <summary>In shared mode, the most students attached at once over all processes and both languages. 0 = work it out
    /// from the memory the host has (see <see cref="SharedSessionMemoryMb"/>), so a class opening together cannot exhaust RAM.</summary>
    public int SharedMaxTotalSessions { get; set; } = 0;

    /// <summary>Memory one attached student costs the shared clangd, used for the automatic total above: measured about 85 MB
    /// with the default C++ template (#include &lt;bits/stdc++.h&gt;) plus about 440 MB for the process itself, so 120 covers a class of 15–40;
    /// with SharedPchStorage=disk it is about 65 MB, so 90 fits.</summary>
    public int SharedSessionMemoryMb { get; set; } = 120;

    /// <summary>Share of the host's memory the automatic total may spend on the shared clangd (the rest is the web app, the judge, the OS).</summary>
    public int SharedMemoryPercent { get; set; } = 40;

    /// <summary>clangd -j for the shared process: how many files it can parse at the same time.</summary>
    public int SharedJobs { get; set; } = 4;

    /// <summary>RLIMIT_DATA for the shared process (MB, 0 = none). It holds many files, so size it for the whole class.</summary>
    public int SharedMemoryLimitMb { get; set; } = 0;

    /// <summary>"memory" or "disk": where the shared clangd keeps each open file's preamble (disk trades speed for RAM).</summary>
    public string SharedPchStorage { get; set; } = "memory";

    /// <summary>The shared process exits this long after its last student left, so it does not sit on memory between classes.</summary>
    public int SharedIdleStopSeconds { get; set; } = 120;

    // ---- clangd on its own node(s) (BeeCoding.Lsp, see DEPLOY.md) ----
    /// <summary>WEB side. Empty (default) = clangd runs on this machine. With one or more URLs (<c>ws://host:5100/lsp/node</c>, <c>wss://</c> across
    /// a network) the web app only authenticates the student and relays the editor's WebSocket to a clangd node. With several, each student is
    /// tied to one by a stable hash of their id (sticky: clangd remembers the open file), and the next one is tried when that node is down.</summary>
    public string[] RemoteUrls { get; set; } = [];

    /// <summary>WEB side. The shared secret sent to the node (<c>Authorization: Bearer</c>); must equal the node's <see cref="NodeToken"/>.</summary>
    public string? RemoteToken { get; set; }

    /// <summary>NODE side (BeeCoding.Lsp). Required: the secret a web app must present. The node does no sign-in of its own, so keep it on a private network too.</summary>
    public string? NodeToken { get; set; }

    /// <summary>NODE side. Shared or one-clangd-per-student when the web app does not say (it passes its Admin → System choice along).</summary>
    public bool NodeShared { get; set; } = true;
}
