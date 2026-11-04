using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using BeeCoding.Services.Lsp;
using Microsoft.Extensions.DependencyInjection;
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
        public readonly TaskCompletionSource<string?> Closed = new();

        private static int _n;

        /// <summary>A new student per call (one bridge is allowed per account); pass a cookie to reuse an account.</summary>
        public static async Task<Client> OpenAsync(TestApp app, string? cookie = null)
        {
            if (cookie is null)
            {
                var r = await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = $"lsp{Interlocked.Increment(ref _n)}@test.dev", password = "Passw0rd!x1", displayName = "Lsp", role = "Student" });
                r.EnsureSuccessStatusCode();
                cookie = string.Join("; ", r.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
            }
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
                    if (r.MessageType == WebSocketMessageType.Close) { Closed.TrySetResult(r.CloseStatusDescription); _ready.TrySetCanceled(); return; }   // "server busy" arrives before "ready"
                    if (JsonNode.Parse(sb.ToString()) is not JsonObject m) continue;
                    if (m["beecoding"]?.GetValue<string>() == "ready") { Uri = m["uri"]!.GetValue<string>(); _ready.TrySetResult(); }
                    else if (m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics") lock (Diagnostics) Diagnostics.Add(m["params"]!.AsObject());
                    else if (m["id"] is JsonValue v && v.TryGetValue<long>(out var id) && _waiting.Remove(id, out var tcs)) tcs.TrySetResult(m);
                }
            }
            catch { /* closed */ }
        }

        public void Leave() => ws.Abort();   // a closing handshake would wait for the read loop; a student just goes away

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

            var a = await Client.OpenAsync(app);
            var b = await Client.OpenAsync(app);
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

    [Fact]
    public async Task A_second_clangd_starts_only_when_the_first_is_full_and_the_total_cap_holds()
    {
        if (!ClangdInstalled) return;
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        Environment.SetEnvironmentVariable("Lsp__SharedMaxSessions", "2");        // two students per process ...
        Environment.SetEnvironmentVariable("Lsp__SharedMaxProcesses", "2");       // ... two processes (room for four)
        Environment.SetEnvironmentVariable("Lsp__SharedMaxTotalSessions", "3");   // and never more than 3 attached at all
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
            var shared = app.Services.GetRequiredService<BeeCoding.Services.Lsp.SharedClangd>();
            Assert.Equal(3, shared.TotalCap);

            var a = await Client.OpenAsync(app);
            var b = await Client.OpenAsync(app);
            Assert.Equal(1, shared.Processes);                                       // the first one still has room
            var c = await Client.OpenAsync(app);
            Assert.Equal(2, shared.Processes);                                       // it was full: a second one started
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client.OpenAsync(app));   // room for a 4th, but 3 is the cap

            // a place is given back when a student leaves
            a.Leave();
            for (int i = 0; i < 50 && shared.ActiveSessions > 2; i++) await Task.Delay(100);
            var d = await Client.OpenAsync(app);
            Assert.NotEqual(c.Uri, d.Uri);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Admin__Emails", null);
            Environment.SetEnvironmentVariable("Lsp__SharedMaxSessions", null);
            Environment.SetEnvironmentVariable("Lsp__SharedMaxProcesses", null);
            Environment.SetEnvironmentVariable("Lsp__SharedMaxTotalSessions", null);
        }
    }

    [Fact]
    public async Task A_second_editor_of_the_same_account_takes_over_and_the_first_is_closed()
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
            var shared = app.Services.GetRequiredService<BeeCoding.Services.Lsp.SharedClangd>();

            var first = await Client.OpenAsync(app, cookie);
            var second = await Client.OpenAsync(app, cookie);
            Assert.Equal("replaced", await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, shared.ActiveSessions);                       // one account, one place
            Assert.NotEqual(first.Uri, second.Uri);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public void The_automatic_total_follows_the_memory_of_the_host_and_can_be_overridden()
    {
        SharedClangd Make(LspOptions o) => new(Microsoft.Extensions.Options.Options.Create(o), Microsoft.Extensions.Logging.Abstractions.NullLogger<SharedClangd>.Instance);
        Assert.Equal(7, Make(new LspOptions { SharedMaxTotalSessions = 7 }).TotalCap);
        var bigger = Make(new LspOptions { SharedSessionMemoryMb = 60 }).TotalCap;
        var smaller = Make(new LspOptions { SharedSessionMemoryMb = 240 }).TotalCap;
        Assert.True(bigger >= 2 * smaller - 1 && smaller >= 4);   // half the cost per student, twice the students (never below 4)
    }
}
