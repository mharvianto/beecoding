using BeeCoding.Lsp;
using BeeCoding.Services.Lsp;

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
app.MapLspNode(token);
app.Run();
