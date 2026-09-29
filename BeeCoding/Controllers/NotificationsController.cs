using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>The navbar bell: reactions/comments on the caller's own wall posts.</summary>
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
        // Joins drop notifications whose board or problem has since been (soft-)deleted.
        var rows = await (from n in _db.Notifications
                          where n.UserId == UserId
                          join b in _db.Boards on n.BoardId equals b.Id
                          join p in _db.Problems on n.ProblemId equals p.Id
                          orderby n.CreatedAt descending
                          select new { n, b.Slug, BoardTitle = b.Title, ProblemTitle = p.Title })
            .Take(30).ToListAsync();

        var items = rows.Select(r => NotificationService.ToDto(r.n, r.Slug, r.BoardTitle, r.ProblemTitle)).ToList();
        return new NotificationListDto(await _notifications.UnreadCountAsync(UserId), items);
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
