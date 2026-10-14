using System.Text.Json;

namespace BeeCoding.Services;

/// <summary>
/// Cloudflare Turnstile, the human check on the sign-in, registration and password-reset forms. Off unless
/// <c>Auth:Turnstile:SiteKey</c> and <c>Auth:Turnstile:SecretKey</c> are set (the site key is public and goes to the
/// browser through <c>/api/auth/config</c>; the secret key stays on the server). A token is accepted only if
/// Cloudflare's siteverify says <c>success</c>, the <c>action</c> is the one the form asked for, and the
/// <c>hostname</c> is one of ours: <c>Auth:Turnstile:Hostnames</c> (comma separated) if set, else the host of
/// <c>App:PublicUrl</c>, else the host the request came in on. Anything else (a missing or oversized token, a network
/// error, a malformed answer) rejects the request. Tokens are single use, so the forms ask for a fresh one every time.
/// </summary>
public class TurnstileService(IConfiguration cfg, IHttpClientFactory http, ILogger<TurnstileService> log)
{
    private const int MaxTokenLength = 2048;

    private string SiteKey => cfg["Auth:Turnstile:SiteKey"]?.Trim() ?? "";
    private string SecretKey => cfg["Auth:Turnstile:SecretKey"]?.Trim() ?? "";
    private string VerifyUrl => cfg["Auth:Turnstile:VerifyUrl"] ?? "https://challenges.cloudflare.com/turnstile/v0/siteverify";   // overridable only so tests can point at a stand-in

    public bool Enabled => SiteKey.Length > 0 && SecretKey.Length > 0;

    /// <summary>The key for the browser's widget, or null while Turnstile is off.</summary>
    public string? PublicSiteKey => Enabled ? SiteKey : null;

    private HashSet<string> AllowedHostnames(string requestHost)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configured = cfg["Auth:Turnstile:Hostnames"];
        if (!string.IsNullOrWhiteSpace(configured))
            foreach (var h in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) set.Add(h);
        else if (Uri.TryCreate(cfg["App:PublicUrl"], UriKind.Absolute, out var pub)) set.Add(pub.Host);
        else set.Add(requestHost);
        return set;
    }

    public async Task<bool> VerifyAsync(string? token, string action, string? remoteIp, string requestHost, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) return false;
        try
        {
            var form = new Dictionary<string, string> { ["secret"] = SecretKey, ["response"] = token };
            if (!string.IsNullOrEmpty(remoteIp)) form["remoteip"] = remoteIp;
            using var res = await http.CreateClient("turnstile").PostAsync(VerifyUrl, new FormUrlEncodedContent(form), ct);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (!(root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True))
            {
                var codes = root.TryGetProperty("error-codes", out var ec) && ec.ValueKind == JsonValueKind.Array
                    ? string.Join(",", ec.EnumerateArray().Select(e => e.GetString())) : "";
                log.LogWarning("Turnstile rejected a token for {Action}: {Codes}", action, codes);   // never the token or the secret
                return false;
            }
            var gotAction = root.TryGetProperty("action", out var a) ? a.GetString() : null;
            var gotHost = root.TryGetProperty("hostname", out var h) ? h.GetString() : null;
            if (gotAction != action || gotHost is null || !AllowedHostnames(requestHost).Contains(gotHost))
            {
                log.LogWarning("Turnstile token for {Action} came back with action {GotAction} and hostname {Host}", action, gotAction, gotHost);
                return false;
            }
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Turnstile verification failed for {Action}", action);
            return false;
        }
    }
}
