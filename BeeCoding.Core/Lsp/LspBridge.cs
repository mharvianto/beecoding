using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Lsp;

/// <summary>
/// WebSocket ⇄ clangd bridge, with no web-framework dependency so both the web app and the standalone clangd node (BeeCoding.Lsp)
/// use it. The browser sends one LSP JSON-RPC message per text frame (no Content-Length); locally this adds/strips the stdio framing
/// for clangd. With <see cref="LspOptions.RemoteUrls"/> set it instead relays the frames, untouched, to a clangd node.
/// </summary>
public sealed class LspBridge
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private const int MaxMessageChars = 1_000_000;   // same cap as a shared-clangd message
    private readonly LspOptions _opt;
    private readonly SharedClangd _shared;
    private readonly ILogger<LspBridge> _log;
    private int _slotsInUse;   // a counter, not a SemaphoreSlim: Lsp:MaxConcurrent can be changed live (Admin → System)
    // One bridge per user and language: a second tab/editor takes over and the older one is closed with reason "replaced".
    private sealed record Mine(CancellationTokenSource Replaced, TaskCompletionSource Done);
    private readonly ConcurrentDictionary<(string, string), Mine> _byUser = new();

    /// <summary>People with an editor attached right now (a person with C and C++ open counts once).</summary>
    public int AttachedStudents => _byUser.Keys.Select(k => k.Item1).Distinct().Count();

    public LspBridge(IOptions<LspOptions> opt, SharedClangd shared, ILogger<LspBridge> log)
    {
        _opt = opt.Value;
        _shared = shared;
        _log = log;
    }

    /// <param name="user">Who is asking (one bridge per user and language).</param>
    /// <param name="shared">Students share one clangd (cap Lsp:SharedMaxSessions) instead of one each (Lsp:MaxConcurrent).</param>
    /// <param name="accept">Accepts the browser's (or the web app's) WebSocket; called once, after the checks.</param>
    public async Task HandleAsync(string user, string lang, string style, bool shared, Func<Task<WebSocket>> accept, CancellationToken aborted)
    {
        var key = (user, lang);
        var me = new Mine(new CancellationTokenSource(), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        Mine? prev = null;
        _byUser.AddOrUpdate(key, me, (_, p) => { prev = p; return me; });
        try
        {
            if (prev is not null)
            {
                prev.Replaced.Cancel();
                await Task.WhenAny(prev.Done.Task, Task.Delay(TimeSpan.FromSeconds(2)));   // let its slot go before we ask for one
            }
            if (_opt.RemoteUrls.Length > 0) await ProxyAsync(user, lang, style, shared, accept, aborted, me.Replaced.Token);
            else await LocalAsync(lang, style, shared, accept, aborted, me.Replaced.Token);
        }
        finally
        {
            me.Done.TrySetResult();
            _byUser.TryRemove(new KeyValuePair<(string, string), Mine>(key, me));
        }
    }

    private async Task<bool> TakeSlotAsync(CancellationToken ct)
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            var n = Volatile.Read(ref _slotsInUse);
            if (n < Math.Max(1, _opt.MaxConcurrent) && Interlocked.CompareExchange(ref _slotsInUse, n + 1, n) == n) return true;
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(50, ct);
        }
    }

    private async Task LocalAsync(string lang, string style, bool shared, Func<Task<WebSocket>> accept, CancellationToken aborted, CancellationToken replaced)
    {
        // Shared mode: students share one clangd and the cap is Lsp:SharedMaxSessions; otherwise each gets a clangd of their own,
        // Lsp:MaxConcurrent at a time. Sessions already open keep their kind.
        if (!shared && !await TakeSlotAsync(aborted))
        {
            using var busy = await accept();
            await busy.CloseAsync(WebSocketCloseStatus.PolicyViolation, "server busy", CancellationToken.None);
            return;
        }

        using var ws = await accept();
        ILspSession? session = null;
        try
        {
            session = shared ? await _shared.OpenAsync(lang, style) : await ClangdSession.StartAsync(_opt, lang, _log, style);
            if (session is null)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "server busy", CancellationToken.None);
                return;
            }

            var closed = new CancellationTokenSource();
            var sendLock = new SemaphoreSlim(1, 1);
            async Task ToClientAsync(string json)
            {
                if (ws.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(json);
                // Delivery runs on the language server's read loop (one per process, shared by every student in shared mode): a client that
                // stops reading must not hold it for ever, so a send that takes longer than this drops that client, not everyone's completions.
                using var timeout = new CancellationTokenSource(SendTimeout);
                try
                {
                    await sendLock.WaitAsync(timeout.Token);
                    try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token); }
                    finally { sendLock.Release(); }
                }
                catch (OperationCanceledException) { ws.Abort(); closed.Cancel(); }
                catch { /* client gone */ }
            }

            session.MessageReceived += ToClientAsync;
            session.Exited += () => closed.Cancel();

            // handshake: tell the client the file URI clangd expects
            await ToClientAsync(JsonSerializer.Serialize(new
            {
                beecoding = "ready",
                uri = session.MainFileUri,
                rootUri = new Uri(session.WorkspaceDir).AbsoluteUri,
            }));

            var idle = TimeSpan.FromSeconds(Math.Max(30, _opt.IdleTimeoutSeconds));
            var buf = new byte[64 * 1024];
            var sb = new StringBuilder();

            while (ws.State == WebSocketState.Open && !closed.IsCancellationRequested)
            {
                using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(closed.Token, aborted, replaced);
                recvCts.CancelAfter(idle);
                WebSocketReceiveResult res;
                try { res = await ws.ReceiveAsync(buf, recvCts.Token); }
                catch (OperationCanceledException) { break; }

                if (res.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                if (sb.Length > MaxMessageChars)   // checked per frame: a message that never ends would otherwise grow without bound
                {
                    await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", CancellationToken.None);
                    break;
                }
                if (!res.EndOfMessage) continue;

                var msg = sb.ToString();
                sb.Clear();
                if (msg.Length > 0) await session.SendAsync(msg, aborted);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LSP bridge error");
        }
        finally
        {
            if (session is not null) await session.DisposeAsync();
            if (!shared) Interlocked.Decrement(ref _slotsInUse);
            await CloseQuietlyAsync(ws, replaced.IsCancellationRequested ? WebSocketCloseStatus.PolicyViolation : WebSocketCloseStatus.NormalClosure,
                replaced.IsCancellationRequested ? "replaced" : "bye");
        }
    }

    // ---- relay to a clangd node -------------------------------------------------------------------------------------------------

    /// <summary>The node list in the order to try for this user: their own node first (stable hash of the id, the same on every web instance),
    /// then the others in turn.</summary>
    public static IEnumerable<string> Candidates(string[] urls, string user)
    {
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(user)) h = (h ^ b) * 16777619;   // FNV-1a: string.GetHashCode differs per process
        var first = (int)(h % (uint)urls.Length);
        for (var i = 0; i < urls.Length; i++) yield return urls[(first + i) % urls.Length];
    }

    private async Task ProxyAsync(string user, string lang, string style, bool shared, Func<Task<WebSocket>> accept, CancellationToken aborted, CancellationToken replaced)
    {
        using var ws = await accept();
        using var remote = await ConnectNodeAsync(user, lang, style, shared, aborted);
        if (remote is null)
        {
            await CloseQuietlyAsync(ws, WebSocketCloseStatus.PolicyViolation, "server busy");
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted, replaced);
        var idle = TimeSpan.FromSeconds(Math.Max(30, _opt.IdleTimeoutSeconds));
        string closeReason = "bye"; var closeStatus = WebSocketCloseStatus.NormalClosure;

        // browser -> node: frames go through as they are; only the size and idle limits are enforced here
        async Task UpAsync()
        {
            var buf = new byte[64 * 1024]; var size = 0;
            while (ws.State == WebSocketState.Open && remote.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                using var recv = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                recv.CancelAfter(idle);
                WebSocketReceiveResult res;
                try { res = await ws.ReceiveAsync(buf, recv.Token); }
                catch (OperationCanceledException) { return; }
                if (res.MessageType == WebSocketMessageType.Close) return;
                size += res.Count;
                if (size > MaxMessageChars) { (closeStatus, closeReason) = (WebSocketCloseStatus.MessageTooBig, "message too large"); return; }
                if (res.EndOfMessage) size = 0;
                await remote.SendAsync(buf.AsMemory(0, res.Count), res.MessageType, res.EndOfMessage, stop.Token);
            }
        }

        // node -> browser: a browser that stops reading must not hold this relay for ever
        async Task DownAsync()
        {
            var buf = new byte[64 * 1024];
            while (ws.State == WebSocketState.Open && remote.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                WebSocketReceiveResult res;
                try { res = await remote.ReceiveAsync(buf, stop.Token); }
                catch (OperationCanceledException) { return; }
                if (res.MessageType == WebSocketMessageType.Close)
                {   // pass the node's reason on ("server busy", "replaced", ...): the browser reacts to it
                    closeStatus = res.CloseStatus ?? WebSocketCloseStatus.NormalClosure; closeReason = res.CloseStatusDescription ?? "";
                    return;
                }
                using var send = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                send.CancelAfter(SendTimeout);
                await ws.SendAsync(buf.AsMemory(0, res.Count), res.MessageType, res.EndOfMessage, send.Token);
            }
        }

        try
        {
            var up = UpAsync(); var down = DownAsync();
            await Task.WhenAny(up, down);
            // Say goodbye to the node and give it a moment to answer, so it ends the session cleanly: cancelling a pending
            // ClientWebSocket receive would abort the connection instead of closing it.
            if (remote.State == WebSocketState.Open)
                try { await remote.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            await Task.WhenAny(down, Task.Delay(TimeSpan.FromSeconds(2)));
            stop.Cancel();
            try { await Task.WhenAll(up, down); } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "LSP relay error"); }
        finally
        {
            if (replaced.IsCancellationRequested) (closeStatus, closeReason) = (WebSocketCloseStatus.PolicyViolation, "replaced");
            await CloseQuietlyAsync(remote, WebSocketCloseStatus.NormalClosure, "bye");
            await CloseQuietlyAsync(ws, closeStatus, closeReason);
        }
    }

    private async Task<ClientWebSocket?> ConnectNodeAsync(string user, string lang, string style, bool shared, CancellationToken ct)
    {
        foreach (var url in Candidates(_opt.RemoteUrls, user))
        {
            var c = new ClientWebSocket();
            c.Options.SetRequestHeader("Authorization", "Bearer " + _opt.RemoteToken);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(3));   // a node that does not answer must not hold the editor: try the next one
            try
            {
                var sep = url.Contains('?') ? "&" : "?";
                await c.ConnectAsync(new Uri($"{url}{sep}user={Uri.EscapeDataString(user)}&lang={lang}&style={Uri.EscapeDataString(style)}&shared={(shared ? 1 : 0)}"), limit.Token);
                return c;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                c.Dispose();
                _log.LogWarning("clangd node {Url} not reachable: {Message}", url, ex.Message);
            }
        }
        return null;
    }

    private static async Task CloseQuietlyAsync(WebSocket ws, WebSocketCloseStatus status, string reason)
    {
        if (ws.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try { await ws.CloseAsync(status, reason, CancellationToken.None); } catch { /* already gone */ }
    }
}
