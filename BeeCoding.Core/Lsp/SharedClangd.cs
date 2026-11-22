using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Lsp;

/// <summary>
/// One clangd process (per language) serving many students instead of one process each. clangd handles many open files
/// at once; this class is its single LSP client and fans the traffic out:
/// <list type="bullet">
/// <item>Each student gets a file of their own in a directory of their own (<c>u1/main.cpp</c>, ...), so every URI is
/// unique and needs no rewriting. Each directory carries its own <c>.clang-format</c>.</item>
/// <item>Request ids are renumbered on the way in and mapped back on the way out; diagnostics go only to the student whose
/// file they are about; clangd's own requests (configuration, progress) are answered here and never reach a browser.</item>
/// <item>A student can only touch their own document and only the editing methods in <see cref="Allowed"/>: another
/// student's URI, <c>workspace/*</c> and the like are dropped.</item>
/// </list>
/// If clangd dies, every session is closed (the browsers reconnect, which starts a fresh process).
/// </summary>
public sealed class SharedClangd(IOptions<LspOptions> opt, ILogger<SharedClangd> log) : IAsyncDisposable
{
    private readonly LspOptions _opt = opt.Value;
    private readonly ILogger<SharedClangd> _log = log;
    private readonly ConcurrentDictionary<string, Lazy<Task<Host>>> _hosts = new();

    private const int MaxMessageChars = 1_000_000;

    /// <summary>Everything a browser editor sends in normal use; anything else is refused.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "textDocument/didOpen", "textDocument/didChange", "textDocument/didClose", "textDocument/didSave",
        "textDocument/completion", "textDocument/hover", "textDocument/signatureHelp",
        "textDocument/formatting", "textDocument/rangeFormatting", "textDocument/definition", "textDocument/documentSymbol",
        "$/cancelRequest",
    };

    // attached students plus the ones being let in (see OpenAsync), over every process
    private int _slots;

    /// <summary>The most students attached at once: Lsp:SharedMaxTotalSessions, or what <see cref="LspOptions.SharedMemoryPercent"/>
    /// of this host's (or container's) memory pays for at <see cref="LspOptions.SharedSessionMemoryMb"/> each.</summary>
    public int TotalCap => _opt.SharedMaxTotalSessions > 0 ? _opt.SharedMaxTotalSessions
        : (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024) * _opt.SharedMemoryPercent / 100 / Math.Max(1, _opt.SharedSessionMemoryMb), 4, 100_000);

    /// <summary>clangd processes running, over both languages.</summary>
    public int Processes => _hosts.Values.Count(l => l.IsValueCreated && l.Value.IsCompletedSuccessfully);

    /// <summary>Students currently attached, over both languages.</summary>
    public int ActiveSessions => _hosts.Values.Where(l => l.IsValueCreated && l.Value.IsCompletedSuccessfully).Sum(l => l.Value.Result.SessionCount);

    /// <summary>A share of the common clangd, or null when it is full or could not start.</summary>
    public async Task<ILspSession?> OpenAsync(string language, string? formatStyle)
    {
        var lang = string.Equals(language, "c", StringComparison.OrdinalIgnoreCase) ? "c" : "cpp";

        // Reserve a place before anything is parsed: a whole class opens at the same moment, long before memory shows it.
        if (Interlocked.Increment(ref _slots) > TotalCap) { Interlocked.Decrement(ref _slots); return null; }
        var session = await OpenOnAProcessAsync(lang, formatStyle);
        if (session is null) Interlocked.Decrement(ref _slots);   // an attached session gives its place back when it closes
        return session;
    }

    // Fill the processes in order: the next one starts only when every earlier one is full.
    private async Task<ILspSession?> OpenOnAProcessAsync(string lang, string? formatStyle)
    {
        for (int i = 0; i < Math.Max(1, _opt.SharedMaxProcesses); i++)
        {
            var key = $"{lang}#{i}";
            var lazy = _hosts.GetOrAdd(key, k => new Lazy<Task<Host>>(() => Host.StartAsync(this, lang, k)));
            Host host;
            try { host = await lazy.Value; }
            catch (Exception ex)
            {
                _hosts.TryRemove(new KeyValuePair<string, Lazy<Task<Host>>>(key, lazy));
                _log.LogWarning(ex, "shared clangd ({Key}) could not start", key);
                return null;
            }
            if (await host.OpenSessionAsync(formatStyle) is { } session) return session;
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _hosts.Values)
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully) await lazy.Value.Result.StopAsync();
    }

    // ---------------------------------------------------------------------------------------------

    private sealed class Host
    {
        private readonly SharedClangd _owner;
        private readonly string _lang;
        private readonly string _key;   // "cpp#0", "cpp#1", ... the slot this process holds in SharedClangd._hosts
        private readonly ClangdSession _proc;
        private readonly ConcurrentDictionary<string, Session> _byUri = new();
        private readonly ConcurrentDictionary<long, (Session S, JsonNode? OrigId)> _pending = new();
        private readonly TaskCompletionSource<JsonNode?> _initResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _nextId = 1000;   // 1 is the initialize request
        private int _seq, _dead;
        private CancellationTokenSource? _stopTimer;

        private Host(SharedClangd owner, string lang, string key, ClangdSession proc) { _owner = owner; _lang = lang; _key = key; _proc = proc; }

        public int SessionCount => _byUri.Count;
        public JsonNode? InitResultNode() => _initResult.Task.IsCompletedSuccessfully ? _initResult.Task.Result?.DeepClone() : null;
        private Lazy<Task<Host>>? Entry => _owner._hosts.TryGetValue(_key, out var l) && l.IsValueCreated && l.Value.IsCompletedSuccessfully && l.Value.Result == this ? l : null;

        public static async Task<Host> StartAsync(SharedClangd owner, string lang, string key)
        {
            var o = owner._opt;
            var proc = await ClangdSession.StartAsync(o, lang, owner._log, "LLVM",
                jobs: Math.Max(1, o.SharedJobs), pchStorage: string.Equals(o.SharedPchStorage, "disk", StringComparison.OrdinalIgnoreCase) ? "disk" : "memory",
                memoryLimitMb: o.SharedMemoryLimitMb);
            var host = new Host(owner, lang, key, proc);
            proc.MessageReceived += host.OnClangdMessageAsync;
            proc.Exited += host.OnExited;
            try
            {
                await proc.SendAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize",
                    ["params"] = new JsonObject
                    {
                        ["processId"] = null,
                        ["rootUri"] = new Uri(proc.WorkspaceDir).AbsoluteUri,
                        // the same capabilities the browser asks for (see lib/cpplsp.js)
                        ["capabilities"] = JsonNode.Parse("""
                            {"textDocument":{"synchronization":{"didSave":false,"willSave":false},
                             "completion":{"completionItem":{"snippetSupport":true,"documentationFormat":["markdown","plaintext"]},"contextSupport":true},
                             "hover":{"contentFormat":["markdown","plaintext"]},
                             "signatureHelp":{"signatureInformation":{"documentationFormat":["markdown","plaintext"]}},
                             "publishDiagnostics":{},"formatting":{}}}
                            """),
                    },
                }.ToJsonString());
                await host._initResult.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await proc.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            }
            catch { await proc.DisposeAsync(); throw; }
            owner._log.LogInformation("Shared clangd ({Key}) started, up to {Max} students", key, o.SharedMaxSessions);
            return host;
        }

        public async Task<ILspSession?> OpenSessionAsync(string? formatStyle)
        {
            if (Volatile.Read(ref _dead) == 1 || _byUri.Count >= _owner._opt.SharedMaxSessions) return null;
            CancelStop();

            var n = Interlocked.Increment(ref _seq);
            var dir = Path.Combine(_proc.WorkspaceDir, "u" + n);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, _lang == "c" ? "main.c" : "main.cpp");
            await File.WriteAllTextAsync(file, "");
            var style = ClangdSession.FormatStyles.Contains(formatStyle, StringComparer.OrdinalIgnoreCase) ? formatStyle! : "LLVM";
            await File.WriteAllTextAsync(Path.Combine(dir, ".clang-format"), $"BasedOnStyle: {style}\n");

            var session = new Session(this, dir, file);
            _byUri[session.MainFileUri] = session;
            if (_byUri.Count > _owner._opt.SharedMaxSessions)   // two opens raced past the check above
            {
                _byUri.TryRemove(session.MainFileUri, out _);   // not Dispose: the caller gives the reserved place back
                try { Directory.Delete(dir, recursive: true); } catch { }
                return null;
            }
            return session;
        }

        // ---- clangd -> students ----
        private async Task OnClangdMessageAsync(string json)
        {
            if (JsonNode.Parse(json) is not JsonObject o) return;
            var method = (string?)o["method"];
            var id = o["id"];

            if (method is null && id is not null)   // a response
            {
                if (id is JsonValue v && v.TryGetValue<long>(out var gid))
                {
                    if (gid == 1) { _initResult.TrySetResult(o["result"]?.DeepClone()); return; }
                    if (_pending.TryRemove(gid, out var p))
                    {
                        o["id"] = p.OrigId?.DeepClone();
                        await p.S.DeliverAsync(o.ToJsonString());
                    }
                }
                return;
            }

            if (method is not null && id is not null)   // clangd asking its client something: answer it here
            {
                JsonNode? result = null;
                if (method == "workspace/configuration")
                {
                    var items = (o["params"]?["items"] as JsonArray)?.Count ?? 1;
                    var nulls = new JsonArray();
                    for (int i = 0; i < items; i++) nulls.Add(null);
                    result = nulls;
                }
                await _proc.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString());
                return;
            }

            if (method == "textDocument/publishDiagnostics"
                && (string?)o["params"]?["uri"] is { } uri && _byUri.TryGetValue(uri, out var owner))
                await owner.DeliverAsync(json);
            // progress, log lines, status: not for any one student
        }

        private void OnExited()
        {
            if (Interlocked.Exchange(ref _dead, 1) == 1) return;
            if (Entry is { } e) _owner._hosts.TryRemove(new KeyValuePair<string, Lazy<Task<Host>>>(_key, e));
            _owner._log.LogWarning("Shared clangd ({Key}) exited; closing {Count} session(s)", _key, _byUri.Count);
            foreach (var s in _byUri.Values) s.RaiseExited();
        }

        // ---- bookkeeping ----
        public long Forward(Session s, JsonNode? origId)
        {
            var gid = Interlocked.Increment(ref _nextId);
            _pending[gid] = (s, origId);
            return gid;
        }

        public long? FindForwarded(Session s, JsonNode? origId) =>
            _pending.Where(kv => kv.Value.S == s && JsonNode.DeepEquals(kv.Value.OrigId, origId)).Select(kv => (long?)kv.Key).FirstOrDefault();

        public Task SendToClangdAsync(string json, CancellationToken ct) => _proc.SendAsync(json, ct);

        public void Remove(Session s)
        {
            if (_byUri.TryRemove(s.MainFileUri, out _)) Interlocked.Decrement(ref _owner._slots);
            foreach (var kv in _pending.Where(kv => kv.Value.S == s)) _pending.TryRemove(kv.Key, out _);
            if (_byUri.IsEmpty) ScheduleStop();
        }

        private void ScheduleStop()
        {
            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _stopTimer, cts)?.Cancel();
            var wait = TimeSpan.FromSeconds(Math.Max(5, _owner._opt.SharedIdleStopSeconds));
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(wait, cts.Token); } catch (OperationCanceledException) { return; }
                if (_byUri.IsEmpty) await StopAsync();   // nobody came back: release the memory
            });
        }

        private void CancelStop() => Interlocked.Exchange(ref _stopTimer, null)?.Cancel();

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _dead, 1) == 1) return;
            if (Entry is { } e) _owner._hosts.TryRemove(new KeyValuePair<string, Lazy<Task<Host>>>(_key, e));
            await _proc.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------

    private sealed class Session(Host host, string dir, string file) : ILspSession
    {
        private int _disposed;
        private bool _opened;

        public string MainFileUri { get; } = new Uri(file).AbsoluteUri;
        public string WorkspaceDir { get; } = dir;
        public event Func<string, Task>? MessageReceived;
        public event Action? Exited;

        public Task DeliverAsync(string json) => MessageReceived is { } h ? h(json) : Task.CompletedTask;
        public void RaiseExited() => Exited?.Invoke();

        private Task Reply(JsonNode? id, JsonNode? result) => id is null ? Task.CompletedTask
            : DeliverAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString());

        private Task ReplyError(JsonNode? id, int code, string message) => id is null ? Task.CompletedTask
            : DeliverAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString());

        /// <summary>A message from this student's browser.</summary>
        public async Task SendAsync(string json, CancellationToken ct = default)
        {
            if (json.Length > MaxMessageChars) return;
            JsonObject? o;
            try { o = JsonNode.Parse(json) as JsonObject; } catch { return; }
            var method = (string?)o?["method"];
            if (o is null || method is null) return;   // a browser never answers anything: server requests are not forwarded to it
            var id = o["id"];

            switch (method)
            {
                // the handshake already happened once for everybody
                case "initialize": await Reply(id, host.InitResultNode()); return;
                case "shutdown": await Reply(id, null); return;
                case "initialized": case "exit": return;
            }
            if (!Allowed.Contains(method))
            {
                if (id is not null) await ReplyError(id, -32601, "Method not supported");
                return;
            }

            if (method == "$/cancelRequest")
            {
                if (host.FindForwarded(this, o["params"]?["id"]) is not { } gid) return;
                o["params"]!["id"] = gid;
            }
            else
            {
                if ((string?)o["params"]?["textDocument"]?["uri"] != MainFileUri) return;   // only this student's own document
                if (id is not null) o["id"] = host.Forward(this, id.DeepClone());
                if (method == "textDocument/didOpen") _opened = true;
                else if (method == "textDocument/didClose") _opened = false;
            }
            await host.SendToClangdAsync(o.ToJsonString(), ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            host.Remove(this);
            if (_opened)
            {
                try
                {
                    await host.SendToClangdAsync(new JsonObject
                    {
                        ["jsonrpc"] = "2.0", ["method"] = "textDocument/didClose",
                        ["params"] = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = MainFileUri } },
                    }.ToJsonString(), CancellationToken.None);
                }
                catch { /* clangd is gone already */ }
            }
            try { Directory.Delete(WorkspaceDir, recursive: true); } catch { }
        }
    }
}
