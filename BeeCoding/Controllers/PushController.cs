using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

public record PushSubscribeDto(string Endpoint, string P256dh, string Auth);
public record PushEndpointDto(string Endpoint);

/// <summary>Web Push subscriptions for the bell (one row per browser) and a test send. See <see cref="WebPushService"/>.</summary>
[ApiController]
[Authorize]
public class PushController(AppDbContext db, WebPushService push) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly WebPushService _push = push;

    [HttpGet("api/push/config")]
    public ActionResult<object> Config() => new { enabled = _push.Enabled, publicKey = _push.PublicVapidKey };

    [HttpPost("api/me/push")]
    public async Task<ActionResult<object>> Subscribe(PushSubscribeDto dto)
    {
        if (!_push.Enabled) return NotFound();
        if (!WebPushService.IsAllowedEndpoint(dto.Endpoint ?? "") || string.IsNullOrWhiteSpace(dto.P256dh) || string.IsNullOrWhiteSpace(dto.Auth)
            || dto.P256dh.Length > 200 || dto.Auth.Length > 100)
            return BadRequest(new { error = "Invalid push subscription." });

        var ua = Request.Headers.UserAgent.ToString();
        var sub = await _db.WebPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint);
        if (sub is null)
        {
            sub = new WebPushSubscription { Endpoint = dto.Endpoint };
            _db.WebPushSubscriptions.Add(sub);
        }
        sub.UserId = UserId;   // the same browser signing in as someone else takes the subscription over
        sub.P256dh = dto.P256dh;
        sub.Auth = dto.Auth;
        sub.UserAgent = ua.Length > 300 ? ua[..300] : ua;
        sub.CreatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // keep the newest few per user
        var extra = await _db.WebPushSubscriptions.Where(s => s.UserId == UserId).OrderByDescending(s => s.CreatedAt)
            .Skip(WebPushService.MaxSubscriptionsPerUser).ToListAsync();
        if (extra.Count > 0) { _db.WebPushSubscriptions.RemoveRange(extra); await _db.SaveChangesAsync(); }
        return new { ok = true };
    }

    [HttpPost("api/me/push/unsubscribe")]
    public async Task<ActionResult<object>> Unsubscribe(PushEndpointDto dto)
    {
        var sub = await _db.WebPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint && s.UserId == UserId);
        if (sub is not null) { _db.WebPushSubscriptions.Remove(sub); await _db.SaveChangesAsync(); }
        return new { ok = true };
    }

    /// <summary>Send a real push to every browser the caller subscribed, so the whole path (VAPID, push service, service worker) is exercised.</summary>
    [HttpPost("api/me/push/test")]
    public async Task<ActionResult<object>> Test()
    {
        if (!_push.Enabled) return NotFound();
        var sent = await _push.SendAsync(UserId, "BeeCoding", "Test notification: push works on this device.", "/", $"beecoding-test-{Guid.NewGuid():N}");
        return new { sent };
    }
}
