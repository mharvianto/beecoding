using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>One-level folders/sessions that group a board's problems. Each carries its own
/// hide flag, exam mode and open/close window (see <see cref="GroupAccess"/>).</summary>
[Authorize]
[Route("api/boards/{slug}/groups")]
public class GroupsController(AppDbContext db, BoardService boards, VisibilityService vis,
    IBoardNotifier notifier, AdminAccess admin) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly VisibilityService _vis = vis;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly AdminAccess _admin = admin;

    [HttpGet]
    public async Task<ActionResult<List<ProblemGroupDto>>> List(string slug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var me = await _boards.GetMembershipAsync(boardId.Value, UserId);
        bool isAdmin = IsAdminUser(_admin);
        if (me is null && !isAdmin) return Forbid();
        bool staff = me is not null ? _vis.IsStaff(me.Role) : isAdmin;

        var now = DateTime.UtcNow;
        var groups = await _db.ProblemGroups
            .Where(g => g.BoardId == boardId.Value)
            .Include(g => g.Problems)
            .OrderBy(g => g.Position).ThenBy(g => g.Id)
            .ToListAsync();

        // Students only learn about groups they can currently open, and only count the
        // problems they can see in them.
        return groups
            .Where(g => staff || (!g.Hidden && !(g.OpensAt > now)))
            .Select(g => ToDto(g, now, staff ? g.Problems.Count : g.Problems.Count(p => !p.Hidden)))
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ProblemGroupDto>> Create(string slug, UpsertProblemGroupDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;
        if (GroupAccess.Validate(dto) is { } bad) return BadRequest(bad);

        var next = (await _db.ProblemGroups.Where(g => g.BoardId == boardId!.Value)
            .Select(g => (int?)g.Position).MaxAsync() ?? -1) + 1;
        var g = new ProblemGroup { BoardId = boardId!.Value, Position = next };
        GroupAccess.Apply(g, dto);
        _db.ProblemGroups.Add(g);
        await _db.SaveChangesAsync();
        await NotifyAsync(boardId.Value);
        return ToDto(g, DateTime.UtcNow, 0);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProblemGroupDto>> Update(string slug, int id, UpsertProblemGroupDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;
        if (GroupAccess.Validate(dto) is { } bad) return BadRequest(bad);

        var g = await _db.ProblemGroups.Include(x => x.Problems)
            .FirstOrDefaultAsync(x => x.Id == id && x.BoardId == boardId!.Value);
        if (g is null) return NotFound();

        GroupAccess.Apply(g, dto);
        await _db.SaveChangesAsync();
        await NotifyAsync(boardId!.Value);
        return ToDto(g, DateTime.UtcNow, g.Problems.Count);
    }

    /// <summary>Deleting a group keeps its problems — they simply become ungrouped.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(string slug, int id)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var g = await _db.ProblemGroups.FirstOrDefaultAsync(x => x.Id == id && x.BoardId == boardId!.Value);
        if (g is null) return NotFound();

        // Ungroup explicitly (rather than relying on the FK's SET NULL) so tracked entities
        // and the SQLite schema behave the same. IgnoreQueryFilters: soft-deleted problems
        // must not be left pointing at a group that no longer exists.
        var members = await _db.Problems.IgnoreQueryFilters().Where(p => p.GroupId == id).ToListAsync();
        foreach (var p in members) p.GroupId = null;
        _db.ProblemGroups.Remove(g);
        await _db.SaveChangesAsync();
        await NotifyAsync(boardId!.Value);
        return NoContent();
    }

    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(string slug, ReorderGroupsDto dto)
    {
        var (boardId, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var byId = await _db.ProblemGroups.Where(g => g.BoardId == boardId!.Value).ToDictionaryAsync(g => g.Id);
        for (var i = 0; i < dto.Order.Count; i++)
            if (byId.TryGetValue(dto.Order[i], out var g)) g.Position = i;

        await _db.SaveChangesAsync();
        await NotifyAsync(boardId!.Value);
        return NoContent();
    }

    private static ProblemGroupDto ToDto(ProblemGroup g, DateTime now, int problemCount) =>
        new(g.Id, g.Title, g.Position, g.Hidden, g.ExamMode, g.OpensAt, g.ClosesAt, GroupAccess.State(g, now), problemCount);

    // Group changes can flip what students see (visibility, exam mode), so refresh every
    // open board the same way the board-wide exam toggle does.
    private async Task NotifyAsync(int boardId)
    {
        var examMode = await _db.Boards.Where(b => b.Id == boardId).Select(b => b.ExamMode).FirstOrDefaultAsync();
        await _notifier.ProblemChangedAsync(boardId);
        await _notifier.ExamModeChangedAsync(boardId, examMode);
    }

    private async Task<(int? boardId, ActionResult? error)> RequireOwnerAsync(string slug)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return (null, NotFound());
        if (board.OwnerId != UserId) return (null, Forbid());
        return (board.Id, null);
    }
}
