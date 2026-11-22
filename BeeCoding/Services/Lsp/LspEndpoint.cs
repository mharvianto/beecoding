using System.Security.Claims;
using BeeCoding.Services;

namespace BeeCoding.Services.Lsp;

/// <summary>The <c>/lsp/cpp</c> WebSocket: checks who is asking and whether the language server is on, then hands the socket to
/// <see cref="LspBridge"/>, which runs clangd here or relays to a clangd node (<see cref="LspOptions.RemoteUrls"/>).</summary>
public class LspEndpoint(PlatformRuntimeConfig runtime, LspBridge bridge)
{
    public async Task HandleAsync(HttpContext ctx)
    {
        if (!runtime.LspEnabled) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (ctx.User?.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

        var lang = ctx.Request.Query["lang"].ToString();
        if (lang is not ("c" or "cpp")) lang = "cpp";
        var style = ctx.Request.Query["style"].ToString();
        var user = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        await bridge.HandleAsync(user, lang, style, runtime.LspShared, () => ctx.WebSockets.AcceptWebSocketAsync(), ctx.RequestAborted);
    }
}
