using System.Security.Cryptography;
using System.Text;
using BeeCoding.Services.Lsp;
using Microsoft.Extensions.Options;

namespace BeeCoding.Lsp;

public static class LspNodeEndpoints
{
    /// <summary><c>/lsp/node</c>: a WebSocket that only a web app holding the shared token may open (needs <c>UseWebSockets</c>).</summary>
    public static void MapLspNode(this WebApplication app, string token) =>
        app.MapGet("/lsp/node", async (HttpContext ctx, LspBridge bridge, IOptions<LspOptions> opt) =>
        {
            var given = ctx.Request.Headers.Authorization.ToString();
            var expected = "Bearer " + token;
            // fixed-time compare over hashes, so neither the content nor the length leaks
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

            var q = ctx.Request.Query;
            var lang = q["lang"].ToString() is "c" ? "c" : "cpp";
            var user = q["user"].ToString();
            if (user.Length is 0 or > 64) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            // the web app passes its Admin > System choice; without it, this node's own default
            var shared = q["shared"].ToString() switch { "1" => true, "0" => false, _ => opt.Value.NodeShared };
            await bridge.HandleAsync(user, lang, q["style"].ToString(), shared, () => ctx.WebSockets.AcceptWebSocketAsync(), ctx.RequestAborted);
        });
}
