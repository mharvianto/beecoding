using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>The navbar bell: reactions/comments on the caller's wall posts, problem reports and their outcomes.</summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db, NotificationService notifications, PlatformRuntimeConfig platform, EmailService email) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly NotificationService _notifications = notifications;

    private const int PageSize = 30;

    /// <summary>The caller's own choices per kind next to what the platform allows. Email is opt-in and needs a verified address.</summary>
    [HttpGet("settings")]
    public async Task<NotifySettingsDto> GetSettings()
    {
        var u = await _db.Users.Where(x => x.Id == UserId).Select(x => new { x.NotifyOffKinds, x.NotifyEmailKinds, x.EmailVerifiedAt }).FirstAsync();
        var off = NotificationKinds.Parse(u.NotifyOffKinds);
        var mail = NotificationKinds.Parse(u.NotifyEmailKinds);
        return new NotifySettingsDto(
            NotificationKinds.All.Select(k => new NotifyKindDto(k.Kind, k.Label, platform.NotifyOffKinds.Contains(k.Kind),
                platform.NotifyEmailKinds.Contains(k.Kind), !off.Contains(k.Kind), mail.Contains(k.Kind))).ToList(),
            email.IsConfigured, u.EmailVerifiedAt != null);
    }

    [HttpPut("settings")]
    public async Task<NotifySettingsDto> PutSettings(List<NotifyChoiceDto> choices)
    {
        var user = await _db.Users.FirstAsync(x => x.Id == UserId);
        user.NotifyOffKinds = NotificationKinds.Join(choices.Where(c => !c.Bell).Select(c => c.Kind));
        user.NotifyEmailKinds = NotificationKinds.Join(choices.Where(c => c.Bell && c.Email).Select(c => c.Kind));   // email only for what the bell shows
        await _db.SaveChangesAsync();
        return await GetSettings();
    }

    /// <summary>One page, newest first; <c>skip</c> pages further back ("load more").</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> List([FromQuery] int skip = 0)
    {
        // Wall notifications whose board or problem was since (soft-)deleted are dropped; report ones always show.
        return new NotificationListDto(await _notifications.UnreadCountAsync(UserId),
            await _notifications.ListAsync(UserId, PageSize, Math.Max(0, skip)), await _notifications.TotalAsync(UserId));
    }

    /// <summary>Delete one notification — only once it has been read, so nothing unseen disappears.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (n is null) return NotFound();
        if (n.ReadAt is null) return Conflict("Read it first.");
        _db.Notifications.Remove(n);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>"Clear read": delete every notification the caller has already read.</summary>
    [HttpDelete("read")]
    public async Task<IActionResult> DeleteRead()
    {
        await _db.Notifications.Where(x => x.UserId == UserId && x.ReadAt != null).ExecuteDeleteAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/read")]
    public async Task<ActionResult<object>> Read(int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (n is null) return NotFound();
        if (n.ReadAt is null) { n.ReadAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        return new { unread = await _notifications.UnreadCountAsync(UserId) };
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<object>> ReadAll()
    {
        await _db.Notifications.Where(x => x.UserId == UserId && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, DateTime.UtcNow));
        return new { unread = 0 };
    }
}
