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

    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> List()
    {
        // Wall notifications whose board or problem was since (soft-)deleted are dropped; report ones always show.
        return new NotificationListDto(await _notifications.UnreadCountAsync(UserId), await _notifications.ListAsync(UserId, 30));
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
