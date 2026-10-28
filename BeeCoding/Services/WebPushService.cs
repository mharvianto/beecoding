using System.Net;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace BeeCoding.Services;

/// <summary>
/// Web Push for the bell, so a notification reaches a user whose tab is closed. Off unless <c>Push:PublicKey</c>,
/// <c>Push:PrivateKey</c> (a VAPID pair) and <c>Push:Subject</c> (a <c>mailto:</c> or https URL) are set; the public key goes
/// to the browser through <c>/api/push/config</c>. Sending never blocks or fails the request that created the notification:
/// it runs on its own scope, errors are logged, and a subscription the push service reports gone (404/410) is deleted.
/// Subscription endpoints are user-supplied URLs the server will POST to, so only the known browser push services are accepted.
/// </summary>
public class WebPushService(IConfiguration cfg, IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<WebPushService> log)
{
    public const int MaxSubscriptionsPerUser = 10;
    private static readonly string[] AllowedHostSuffixes =
        [".googleapis.com", ".push.services.mozilla.com", ".push.apple.com", ".notify.windows.com"];

    private string PublicKey => cfg["Push:PublicKey"]?.Trim() ?? "";
    private string PrivateKey => cfg["Push:PrivateKey"]?.Trim() ?? "";
    private string Subject => cfg["Push:Subject"]?.Trim() ?? "";

    public bool Enabled => PublicKey.Length > 0 && PrivateKey.Length > 0 && Subject.Length > 0;
    public string? PublicVapidKey => Enabled ? PublicKey : null;

    public static bool IsAllowedEndpoint(string endpoint)
    {
        if (endpoint.Length > 1000 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        return AllowedHostSuffixes.Any(s => host.EndsWith(s) || host == s[1..]);
    }

    /// <summary>Title, body and router path (the service worker prefixes the app's base path) for a bell item; mirrors notificationText in the SPA.</summary>
    public static (string Title, string Body, string Url) Describe(NotificationDto n)
    {
        var who = n.ActorIsStaff ? $"{n.ActorName} 👨‍🏫" : n.ActorName;
        var body = n.Kind switch
        {
            "report" => $"{n.ActorName} reported a problem — {n.Snippet}",
            "report-update" => $"Your report was {(n.Emoji == "✅" ? "resolved" : "dismissed")} by {n.ActorName}{(string.IsNullOrEmpty(n.Snippet) ? "" : $": “{n.Snippet}”")}",
            "comment" => $"{who} commented: “{n.Snippet}”",
            _ => n.Count > 1 ? $"{n.Count} new reactions on your card (latest {n.Emoji} from {who})" : $"{who} reacted {n.Emoji} to your card",
        };
        var url = !string.IsNullOrEmpty(n.Link) ? n.Link : $"/boards/{n.BoardSlug}?problem={n.ProblemId}&post={n.PostId}";
        var title = string.IsNullOrEmpty(n.ProblemTitle) ? "BeeCoding" : n.ProblemTitle;
        return (title, body, url);
    }

    /// <summary>Fire and forget: deliver a bell item to every browser the user subscribed.</summary>
    public void Queue(int userId, NotificationDto dto)
    {
        if (!Enabled) return;
        var (title, body, url) = Describe(dto);
        _ = Task.Run(() => SendAsync(userId, title, body, url, $"beecoding-{dto.Id}"));
    }

    /// <summary>Deliver to every subscription of a user; returns how many accepted it.</summary>
    public async Task<int> SendAsync(int userId, string title, string body, string url, string tag)
    {
        if (!Enabled) return 0;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subs = await db.WebPushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
            if (subs.Count == 0) return 0;

            var payload = JsonSerializer.Serialize(new { title, body, url, tag });
            var vapid = new VapidDetails(Subject, PublicKey, PrivateKey);
            var client = new WebPushClient(http.CreateClient("webpush"));
            var sent = 0;
            foreach (var s in subs)
            {
                try
                {
                    await client.SendNotificationAsync(new PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, vapid);
                    sent++;
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    db.WebPushSubscriptions.Remove(s);   // the browser unsubscribed or the subscription expired
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Web push to subscription {Id} failed", s.Id);
                }
            }
            await db.SaveChangesAsync();
            return sent;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Web push for user {UserId} failed", userId);
            return 0;
        }
    }
}
