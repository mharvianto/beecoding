using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class OAuthTests
{
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string Verifier = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";   // 43 characters
    private static string Challenge(string v) => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(v))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<string> Register(TestApp app, string redirect = Callback)
    {
        var r = await app.CreateClient().PostAsJsonAsync("/oauth/register", new { client_name = "Claude", redirect_uris = new[] { redirect }, token_endpoint_auth_method = "none" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("client_id").GetString()!;
    }

    private static async Task<string> Approve(HttpClient teacher, string clientId, string challenge, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var r = await teacher.PostAsJsonAsync("/api/oauth/authorize", new { clientId, redirectUri = Callback, state = "st1", codeChallenge = challenge, codeChallengeMethod = "S256", approve = true });
        Assert.Equal(expect, r.StatusCode);
        if (expect != HttpStatusCode.OK) return "";
        var to = new Uri((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectTo").GetString()!);
        Assert.StartsWith(Callback, to.ToString());
        Assert.Contains("state=st1", to.Query);
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(to.Query)["code"].ToString();
    }

    private static Task<HttpResponseMessage> Token(TestApp app, Dictionary<string, string> form) => app.CreateClient().PostAsync("/oauth/token", new FormUrlEncodedContent(form));
    private static async Task<HttpStatusCode> Ping(TestApp app, string access)
    {
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return (await c.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" })).StatusCode;
    }

    [Fact]
    public async Task The_whole_connect_flow_with_pkce_refresh_and_revocation()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();

            // discovery, and the 401 points the assistant at it
            var meta = await app.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server");
            Assert.EndsWith("/oauth/token", meta.GetProperty("token_endpoint").GetString());
            Assert.Equal("S256", meta.GetProperty("code_challenge_methods_supported")[0].GetString());
            Assert.EndsWith("/mcp", (await app.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource")).GetProperty("resource").GetString());
            var unauth = await app.CreateClient().PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" });
            Assert.Contains("resource_metadata=", unauth.Headers.WwwAuthenticate.ToString());

            // registration only accepts known redirect addresses (the assistant's own, or this machine)
            var evil = await app.CreateClient().PostAsJsonAsync("/oauth/register", new { client_name = "x", redirect_uris = new[] { "https://evil.example/cb" } });
            Assert.Equal(HttpStatusCode.BadRequest, evil.StatusCode);
            await Register(app, "http://localhost:6274/callback");
            var clientId = await Register(app);

            // the browser step: an unchecked redirect is never followed; a good one goes to the consent page
            var noRedirect = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.BadRequest, (await noRedirect.GetAsync($"/oauth/authorize?client_id={clientId}&redirect_uri=https://evil.example/cb")).StatusCode);
            var go = await noRedirect.GetAsync($"/oauth/authorize?client_id={clientId}&redirect_uri={Callback}&code_challenge=x&state=s");
            Assert.Equal(HttpStatusCode.Redirect, go.StatusCode);
            Assert.Contains("/oauth/consent?client_id=", go.Headers.Location!.ToString());

            // only a signed-in teacher can approve
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
            await Approve(kid, clientId, Challenge(Verifier), HttpStatusCode.Forbidden);
            var teacher = app.CreateClient();
            (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var info = await teacher.GetFromJsonAsync<JsonElement>($"/api/oauth/client?clientId={clientId}&redirectUri={Uri.EscapeDataString(Callback)}");
            Assert.Equal("Claude", info.GetProperty("name").GetString());

            // a wrong verifier fails and burns the code; a fresh code with the right one works
            var bad = await Approve(teacher, clientId, Challenge(Verifier));
            var form = (string code, string verifier) => new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code, ["redirect_uri"] = Callback, ["code_verifier"] = verifier };
            Assert.Equal(HttpStatusCode.BadRequest, (await Token(app, form(bad, "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Token(app, form(bad, Verifier))).StatusCode);   // burnt

            var code = await Approve(teacher, clientId, Challenge(Verifier));
            var ok = await Token(app, form(code, Verifier));
            ok.EnsureSuccessStatusCode();
            var t1 = await ok.Content.ReadFromJsonAsync<JsonElement>();
            var access = t1.GetProperty("access_token").GetString()!; var refresh = t1.GetProperty("refresh_token").GetString()!;
            Assert.Equal(HttpStatusCode.BadRequest, (await Token(app, form(code, Verifier))).StatusCode);   // a code is used once
            Assert.Equal(HttpStatusCode.OK, await Ping(app, access));

            // refresh rotates both tokens; the old ones stop working
            var rf = (string r) => new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = r };
            var t2 = await (await Token(app, rf(refresh))).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.Unauthorized, await Ping(app, access));
            Assert.Equal(HttpStatusCode.OK, await Ping(app, t2.GetProperty("access_token").GetString()!));
            Assert.Equal(HttpStatusCode.BadRequest, (await Token(app, rf(refresh))).StatusCode);

            // it shows in Account as a connected app, and revoking it ends it
            var list = await teacher.GetFromJsonAsync<JsonElement>("/api/me/api-tokens");
            Assert.True(list[0].GetProperty("connected").GetBoolean());
            (await teacher.DeleteAsync($"/api/me/api-tokens/{list[0].GetProperty("id").GetInt32()}")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, await Ping(app, t2.GetProperty("access_token").GetString()!));
            Assert.Equal(HttpStatusCode.BadRequest, (await Token(app, rf(t2.GetProperty("refresh_token").GetString()!))).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
