using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BeeCoding.Services;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Lsp;

/// <summary>
/// WebSocket ⇄ clangd bridge. The browser sends one LSP JSON-RPC message per text frame
/// (no Content-Length); this adds/strips the stdio framing for clangd.
/// </summary>
public class LspEndpoint
{
    private readonly LspOptions _opt;
    private readonly PlatformRuntimeConfig _runtime;
    private readonly ILogger<LspEndpoint> _log;
    private readonly SemaphoreSlim _slots;
    private readonly SharedClangd _shared;
    // One bridge per user and language: a second tab/editor takes over and the older one is closed with reason "replaced".
    private sealed record Mine(CancellationTokenSource Replaced, TaskCompletionSource Done);
    private readonly ConcurrentDictionary<(string, string), Mine> _byUser = new();

    public LspEndpoint(IOptions<LspOptions> opt, PlatformRuntimeConfig runtime, SharedClangd shared, ILogger<LspEndpoint> log)
    {
        _shared = shared;
        _opt = opt.Value;
        _runtime = runtime;
        _log = log;
        _slots = new SemaphoreSlim(Math.Max(1, _opt.MaxConcurrent));
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        if (!_runtime.LspEnabled) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (ctx.User?.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

        var lang = ctx.Request.Query["lang"].ToString();
        if (lang is not ("c" or "cpp")) lang = "cpp";
        var style = ctx.Request.Query["style"].ToString();

        var key = (ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "", lang);
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
            await BridgeAsync(ctx, lang, style, me.Replaced.Token);
        }
        finally
        {
            me.Done.TrySetResult();
            _byUser.TryRemove(new KeyValuePair<(string, string), Mine>(key, me));
        }
    }

    private async Task BridgeAsync(HttpContext ctx, string lang, string style, CancellationToken replaced)
    {
        // Shared mode (Admin > System): students share one clangd and the cap is Lsp:SharedMaxSessions;
        // otherwise each gets a clangd of their own, Lsp:MaxConcurrent at a time. Sessions already open keep their kind.
        bool shared = _runtime.LspShared;
        if (!shared && !await _slots.WaitAsync(TimeSpan.FromSeconds(2), ctx.RequestAborted))
        {
            using var busy = await ctx.WebSockets.AcceptWebSocketAsync();
            await busy.CloseAsync(WebSocketCloseStatus.PolicyViolation, "server busy", CancellationToken.None);
            return;
        }

        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        ILspSession? session = null;
        try
        {
            session = shared ? await _shared.OpenAsync(lang, style) : await ClangdSession.StartAsync(_opt, lang, _log, style);
            if (session is null)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "server busy", CancellationToken.None);
                return;
            }

            var sendLock = new SemaphoreSlim(1, 1);
            async Task ToClientAsync(string json)
            {
                if (ws.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(json);
                await sendLock.WaitAsync();
                try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { /* client gone */ }
                finally { sendLock.Release(); }
            }

            session.MessageReceived += ToClientAsync;
            var closed = new CancellationTokenSource();
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
                using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(closed.Token, ctx.RequestAborted, replaced);
                recvCts.CancelAfter(idle);
                WebSocketReceiveResult res;
                try { res = await ws.ReceiveAsync(buf, recvCts.Token); }
                catch (OperationCanceledException) { break; }

                if (res.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                if (!res.EndOfMessage) continue;

                var msg = sb.ToString();
                sb.Clear();
                if (msg.Length > 0) await session.SendAsync(msg, ctx.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LSP bridge error");
        }
        finally
        {
            if (session is not null) await session.DisposeAsync();
            if (!shared) _slots.Release();
            if (ws.State == WebSocketState.Open)
                try
                {
                    if (replaced.IsCancellationRequested) await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "replaced", CancellationToken.None);
                    else await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch { }
        }
    }
}
