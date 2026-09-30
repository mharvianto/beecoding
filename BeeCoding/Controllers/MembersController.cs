using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[Authorize]
[Route("api/boards/{slug}/members")]
public class MembersController(AppDbContext db, BoardService boards, VisibilityService vis, IBoardNotifier notifier, AuditLog audit) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly VisibilityService _vis = vis;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly AuditLog _audit = audit;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MemberDto>>> List(string slug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var me = await _boards.GetMembershipAsync(boardId.Value, UserId);
        if (me is null) return Forbid();
        bool staff = _vis.IsStaff(me.Role);

        var members = await _db.BoardMemberships
            .Where(m => m.BoardId == boardId.Value)
            .Include(m => m.User)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.User!.DisplayName)
            .ToListAsync();

        return members
            .Select(m => new MemberDto(m.UserId, m.User!.DisplayName, m.Role.ToString(),
                staff && m.HiddenByTeacher))
            .ToList();
    }

    /// <summary>Feature 5 (per student): owner hides/shows one student's cells from other students.</summary>
    [HttpPatch("{userId:int}")]
    public async Task<ActionResult<MemberDto>> Update(string slug, int userId, UpdateMemberDto dto)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        if (board.OwnerId != UserId) return Forbid();

        var target = await _db.BoardMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.BoardId == board.Id && m.UserId == userId);
        if (target is null) return NotFound();
        if (target.Role != MembershipRole.Student) return BadRequest("Only students can be hidden.");

        target.HiddenByTeacher = dto.HiddenByTeacher;
        await _db.SaveChangesAsync();
        await _notifier.MemberVisibilityChangedAsync(board.Id, userId, dto.HiddenByTeacher);

        return new MemberDto(target.UserId, target.User!.DisplayName, target.Role.ToString(), target.HiddenByTeacher);
    }

    /// <summary>
    /// Owner removes a student from the board. Only the membership goes: their submissions and
    /// wall posts are kept (so re-adding them restores everything) and simply stop showing while
    /// they aren't a member. They can rejoin with the join code unless it is changed.
    /// </summary>
    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> Remove(string slug, int userId)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        if (board.OwnerId != UserId) return Forbid();

        var target = await _db.BoardMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.BoardId == board.Id && m.UserId == userId);
        if (target is null) return NotFound();
        if (target.Role != MembershipRole.Student) return BadRequest("Only students can be removed.");

        _db.BoardMemberships.Remove(target);
        await _db.SaveChangesAsync();
        // Bell items pointing into a board they can no longer open would only lead to a 403.
        await _db.Notifications.Where(n => n.UserId == userId && n.BoardId == board.Id).ExecuteDeleteAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "remove-member", "Board", board.Id, $"{target.User!.DisplayName} removed from {board.Title}");

        await _notifier.MemberRemovedAsync(userId, board.Id, board.Slug, board.Title);
        await _notifier.ProgressChangedAsync(board.Id, 0, userId);   // staff grids drop the row
        await _notifier.WallChangedAsync(board.Id);
        return NoContent();
    }
}
