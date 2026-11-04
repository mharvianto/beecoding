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
public class NotificationsController(AppDbContext db, NotificationService notifications) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly NotificationService _notifications = notifications;

    private const int PageSize = 30;

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
