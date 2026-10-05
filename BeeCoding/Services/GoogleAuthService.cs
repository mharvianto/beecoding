using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeeCoding.Services;

/// <summary>Who Google says the user is (from the verified-by-transport id_token).</summary>
public record GoogleIdentity(string Subject, string Email, bool EmailVerified, string Name, string? Nonce);

/// <summary>
/// "Sign in with Google" via the OpenID Connect authorization-code flow with PKCE. Off unless
/// <c>Auth:Google:ClientId</c> and <c>Auth:Google:ClientSecret</c> are set. The id_token comes straight from
/// Google's token endpoint over TLS in the back channel, so (OIDC Core §3.1.3.7) its claims are checked
/// (issuer, audience, expiry, nonce by the caller) rather than its signature.
/// <c>AuthUrl</c>/<c>TokenUrl</c>/<c>Issuer</c> exist only so tests can point at a stand-in provider.
/// </summary>
public class GoogleAuthService(IConfiguration cfg, IHttpClientFactory http, ILogger<GoogleAuthService> log)
{
    private string ClientId => cfg["Auth:Google:ClientId"]?.Trim() ?? "";
    private string ClientSecret => cfg["Auth:Google:ClientSecret"]?.Trim() ?? "";
    private string AuthUrl => cfg["Auth:Google:AuthUrl"] ?? "https://accounts.google.com/o/oauth2/v2/auth";
    private string TokenUrl => cfg["Auth:Google:TokenUrl"] ?? "https://oauth2.googleapis.com/token";

    public bool Enabled => ClientId.Length > 0 && ClientSecret.Length > 0;

    public static string RandomToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = RandomToken(48);
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public string AuthorizeUrl(string redirectUri, string state, string nonce, string codeChallenge)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = ClientId, ["redirect_uri"] = redirectUri, ["response_type"] = "code",
            ["scope"] = "openid email profile", ["state"] = state, ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge, ["code_challenge_method"] = "S256", ["prompt"] = "select_account",
        };
        return AuthUrl + (AuthUrl.Contains('?') ? "&" : "?") + string.Join("&", q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<GoogleIdentity?> ExchangeAsync(string code, string redirectUri, string verifier)
    {
        try
        {
            using var client = http.CreateClient("google");
            using var res = await client.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = ClientId, ["client_secret"] = ClientSecret,
                ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            }));
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) { log.LogWarning("Google token exchange failed: {Status} {Body}", (int)res.StatusCode, body); return null; }

            using var doc = JsonDocument.Parse(body);
            var idToken = doc.RootElement.GetProperty("id_token").GetString();
            var parts = idToken?.Split('.');
            if (parts is not { Length: 3 }) return null;
            using var claims = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var c = claims.RootElement;

            var iss = c.GetProperty("iss").GetString();
            var expectedIss = cfg["Auth:Google:Issuer"];
            if (expectedIss is null ? iss is not ("https://accounts.google.com" or "accounts.google.com") : iss != expectedIss) return null;
            var aud = c.GetProperty("aud");
            if (!(aud.ValueKind == JsonValueKind.String ? aud.GetString() == ClientId : aud.EnumerateArray().Any(a => a.GetString() == ClientId))) return null;
            if (c.GetProperty("exp").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;

            var email = c.TryGetProperty("email", out var e) ? e.GetString() : null;
            if (string.IsNullOrWhiteSpace(email)) return null;
            bool verified = c.TryGetProperty("email_verified", out var v)
                && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));
            var name = c.TryGetProperty("name", out var n) ? n.GetString() : null;
            var nonce = c.TryGetProperty("nonce", out var nn) ? nn.GetString() : null;
            return new GoogleIdentity(c.GetProperty("sub").GetString()!, email.Trim().ToLowerInvariant(), verified,
                string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim(), nonce);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            log.LogWarning(ex, "Google sign-in: could not complete the token exchange");
            return null;
        }
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
