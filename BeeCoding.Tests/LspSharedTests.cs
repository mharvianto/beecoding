using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>Shared clangd end to end with the real clangd. Skipped (returns at once) on a machine without clangd.</summary>
[Collection("app")]
public class LspSharedTests
{
    private static bool ClangdInstalled =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, "clangd")));

    private sealed class Client(WebSocket ws)
    {
        public string Uri = "";
        public readonly List<JsonObject> Diagnostics = [];
        private readonly Dictionary<long, TaskCompletionSource<JsonObject>> _waiting = new();
        private long _id;
        private readonly TaskCompletionSource _ready = new();

        public static async Task<Client> OpenAsync(TestApp app, string cookie)
        {
            var wsc = app.Server.CreateWebSocketClient();
            wsc.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
            var c = new Client(await wsc.ConnectAsync(new Uri("ws://localhost/lsp/cpp?lang=cpp"), CancellationToken.None));
            _ = c.ReadLoop();
            await c._ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return c;
        }

        private async Task ReadLoop()
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult r;
                    do { r = await ws.ReceiveAsync(buf, CancellationToken.None); sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (JsonNode.Parse(sb.ToString()) is not JsonObject m) continue;
                    if (m["beecoding"]?.GetValue<string>() == "ready") { Uri = m["uri"]!.GetValue<string>(); _ready.TrySetResult(); }
                    else if (m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics") lock (Diagnostics) Diagnostics.Add(m["params"]!.AsObject());
                    else if (m["id"] is JsonValue v && v.TryGetValue<long>(out var id) && _waiting.Remove(id, out var tcs)) tcs.TrySetResult(m);
                }
            }
            catch { /* closed */ }
        }

        public Task Send(JsonObject o) { o["jsonrpc"] = "2.0"; return ws.SendAsync(Encoding.UTF8.GetBytes(o.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None); }

        public async Task<JsonObject?> Request(string method, JsonNode? p, int timeoutMs = 20000)
        {
            var id = ++_id; var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously); _waiting[id] = tcs;
            await Send(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = p });
            try { return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)); } catch (TimeoutException) { return null; }
        }

        public async Task OpenDocument(string text)
        {
            await Request("initialize", JsonNode.Parse("""{"processId":null,"capabilities":{}}"""));
            await Send(new JsonObject { ["method"] = "initialized", ["params"] = new JsonObject() });
            await Send(new JsonObject { ["method"] = "textDocument/didOpen", ["params"] = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri, ["languageId"] = "cpp", ["version"] = 1, ["text"] = text } } });
        }

        public async Task<string> WaitForDiagnostics()
        {
            for (int i = 0; i < 200; i++) { lock (Diagnostics) if (Diagnostics.Count > 0) return Diagnostics[^1]["diagnostics"]!.ToJsonString(); await Task.Delay(100); }
            return "";
        }
    }

    [Fact]
    public async Task Two_students_share_one_clangd_without_seeing_each_other()
    {
        if (!ClangdInstalled) return;
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var http = app.CreateClient();
            var login = await http.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
            login.EnsureSuccessStatusCode();
            var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
            (await http.PutAsJsonAsync("/api/admin-ui/runtime-config", new
            {
                lspEnabled = true, judgeRateLimitMs = 1500, studentMaxProblems = 50, imageMaxKb = 1024, imageMaxPerUser = 100,
                notificationReadRetentionDays = 7, lspShared = true,
            })).EnsureSuccessStatusCode();

            var a = await Client.OpenAsync(app, cookie);
            var b = await Client.OpenAsync(app, cookie);
            Assert.NotEqual(a.Uri, b.Uri);   // each student has a document of their own

            await a.OpenDocument("int main() { int x = 1; return x; }\n");
            await b.OpenDocument("int main() { int y = undeclared_in_b; return y; }\n");
            var diagA = await a.WaitForDiagnostics();
            var diagB = await b.WaitForDiagnostics();
            Assert.Contains("undeclared_in_b", diagB);
            Assert.DoesNotContain("undeclared_in_b", diagA);                 // B's error never reaches A
            Assert.All(a.Diagnostics, d => Assert.Equal(a.Uri, d["uri"]!.GetValue<string>()));

            // A cannot reach B's document, nor any method outside the editing set
            Assert.Null(await a.Request("textDocument/hover", JsonNode.Parse($$$"""{"textDocument":{"uri":"{{{b.Uri}}}"},"position":{"line":0,"character":1}}"""), 2000));
            var other = await a.Request("workspace/executeCommand", JsonNode.Parse("""{"command":"x"}"""), 5000);
            Assert.Equal(-32601, other?["error"]?["code"]?.GetValue<int>());

            // ids are renumbered on the way to clangd and mapped back: both answer their own hover
            var hoverA = await a.Request("textDocument/hover", JsonNode.Parse($$$"""{"textDocument":{"uri":"{{{a.Uri}}}"},"position":{"line":0,"character":5}}"""));
            Assert.NotNull(hoverA);
            Assert.Equal(4, hoverA!["id"]!.GetValue<int>());   // A's 4th request: its own numbering, not the one clangd saw
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
