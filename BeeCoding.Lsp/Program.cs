using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using BeeCoding.Services.Lsp;
using Microsoft.Extensions.Options;

// A clangd node: runs the language servers for the BeeCoding web app(s) so their memory is not shared with the web tier or the judge
// (DEPLOY.md, "clangd on its own node"). It has NO database, sign-in or API: the web app authenticates the student and relays the editor's
// WebSocket here with a shared secret (Lsp:NodeToken). Keep it on a private network (or wss:// behind a proxy).

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<LspOptions>(builder.Configuration.GetSection("Lsp"));
builder.Services.AddSingleton<SharedClangd>();
builder.Services.AddSingleton<LspBridge>();

var token = builder.Configuration["Lsp:NodeToken"];
if (string.IsNullOrWhiteSpace(token))
    throw new InvalidOperationException("Lsp:NodeToken is required: it is the only thing that keeps strangers from running clangd on this machine.");
if (builder.Configuration.GetSection("Lsp:RemoteUrls").GetChildren().Any())
    throw new InvalidOperationException("Lsp:RemoteUrls must be empty on a clangd node (it would relay to another node instead of running clangd).");

var app = builder.Build();
app.UseWebSockets();
app.MapGet("/health", () => "ok");

app.MapGet("/lsp/node", async (HttpContext ctx, LspBridge bridge, IOptions<LspOptions> opt) =>
{
    var given = ctx.Request.Headers.Authorization.ToString();
    var expected = "Bearer " + token;
    // fixed-time compare, and a length that does not leak
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

app.Run();
