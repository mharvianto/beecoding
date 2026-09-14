using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Lsp;

/// <summary>
/// WebSocket ⇄ clangd bridge. The browser sends one LSP JSON-RPC message per text frame
/// (no Content-Length); this adds/strips the stdio framing for clangd.
/// </summary>
public class LspEndpoint
{
    private readonly LspOptions _opt;
    private readonly ILogger<LspEndpoint> _log;
    private readonly SemaphoreSlim _slots;

    public LspEndpoint(IOptions<LspOptions> opt, ILogger<LspEndpoint> log)
    {
        _opt = opt.Value;
        _log = log;
        _slots = new SemaphoreSlim(Math.Max(1, _opt.MaxConcurrent));
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        if (!_opt.Enabled) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (ctx.User?.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

        var lang = ctx.Request.Query["lang"].ToString();
        if (lang is not ("c" or "cpp")) lang = "cpp";
        var style = ctx.Request.Query["style"].ToString();

        if (!await _slots.WaitAsync(TimeSpan.FromSeconds(2), ctx.RequestAborted))
        {
            using var busy = await ctx.WebSockets.AcceptWebSocketAsync();
            await busy.CloseAsync(WebSocketCloseStatus.PolicyViolation, "server busy", CancellationToken.None);
            return;
        }

        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        ClangdSession? session = null;
        try
        {
            session = await ClangdSession.StartAsync(_opt, lang, _log, style);

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
                using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(closed.Token, ctx.RequestAborted);
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
            _slots.Release();
            if (ws.State == WebSocketState.Open)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }
}
