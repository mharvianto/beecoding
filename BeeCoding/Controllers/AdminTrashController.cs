using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lsp;
using BeeCoding.Services.Realtime;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;


namespace BeeCoding.Controllers;

/// <summary>Admin panel: browse, restore and purge soft-deleted rows. Same route prefix as <see cref="AdminUiController"/>.</summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui")]
public class AdminTrashController(AppDbContext db, AuditLog audit) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly AuditLog _audit = audit;

    // ---- trash: browse + restore/purge soft-deleted rows ---------------------
    /// <summary>One trash category at a time, paginated — kind is "users" | "boards" |
    /// "problems" | "bank".</summary>
    [HttpGet("trash/{kind}")]
    public async Task<IActionResult> Trash(string kind, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var n = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        switch (kind)
        {
            case "users":
            {
                var query = _db.Users.Where(u => u.DeletedAt != null);
                if (n is not null)
                    query = query.Where(u => EF.Functions.Like(u.Email, $"%{n}%") || EF.Functions.Like(u.DisplayName, $"%{n}%"));
                var total = await query.CountAsync();
                var rows = await query.OrderByDescending(u => u.DeletedAt).Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(u => new AdminTrashUserRow(u.Id, u.Email, u.DisplayName, u.DeletedAt!.Value)).ToListAsync();
                return Ok(new AdminPageDto<AdminTrashUserRow>(rows, total, page, pageSize));
            }
            case "boards":
            {
                var query = _db.Boards.IgnoreQueryFilters().Where(b => b.DeletedAt != null);
                if (n is not null) query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%"));
                var total = await query.CountAsync();
                var rows = await query.Include(b => b.Owner).OrderByDescending(b => b.DeletedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(b => new AdminTrashBoardRow(b.Slug, b.Title, b.Owner != null ? b.Owner.Email : "?", b.DeletedAt!.Value))
                    .ToListAsync();
                return Ok(new AdminPageDto<AdminTrashBoardRow>(rows, total, page, pageSize));
            }
            case "problems":
            {
                var query = _db.Problems.IgnoreQueryFilters().Where(p => p.DeletedAt != null);
                if (n is not null) query = query.Where(p => EF.Functions.Like(p.Title, $"%{n}%"));
                var total = await query.CountAsync();
                var problems = await query.OrderByDescending(p => p.DeletedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                var boardIds = problems.Select(p => p.BoardId).Distinct().ToList();
                var boardById = await _db.Boards.IgnoreQueryFilters().Where(b => boardIds.Contains(b.Id))
                    .Select(b => new { b.Id, b.Slug, b.Title }).ToDictionaryAsync(b => b.Id);
                var rows = problems.Select(p =>
                {
                    boardById.TryGetValue(p.BoardId, out var b);
                    return new AdminTrashProblemRow(p.Slug, p.Title, b?.Slug ?? "", b?.Title ?? "(deleted board)", p.DeletedAt!.Value);
                }).ToList();
                return Ok(new AdminPageDto<AdminTrashProblemRow>(rows, total, page, pageSize));
            }
            case "bank":
            {
                var query = _db.BankProblems.IgnoreQueryFilters().Where(b => b.DeletedAt != null);
                if (n is not null) query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%"));
                var total = await query.CountAsync();
                var rows = await query.Include(b => b.Owner).OrderByDescending(b => b.DeletedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(b => new AdminTrashBankRow(b.Slug, b.Title, b.Owner != null ? b.Owner.Email : "?", b.DeletedAt!.Value))
                    .ToListAsync();
                return Ok(new AdminPageDto<AdminTrashBankRow>(rows, total, page, pageSize));
            }
            default:
                return BadRequest("kind must be users, boards, problems, or bank.");
        }
    }

    private static readonly string[] TrashKinds = { "users", "boards", "problems", "bank" };

    /// <summary>Bulk restore, one trash category at a time — ids are user ids (users) or
    /// slugs (boards/problems/bank), matching what the Trash tab's rows carry.</summary>
    [HttpPost("trash/{kind}/restore")]
    public async Task<ActionResult<AdminTrashBulkResult>> BulkRestoreTrash(string kind, AdminTrashBulkDto dto)
    {
        if (!TrashKinds.Contains(kind)) return BadRequest("kind must be users, boards, problems, or bank.");

        int count = 0;
        var errors = new List<string>();
        foreach (var id in (dto.Ids ?? new()).Distinct())
        {
            try
            {
                switch (kind)
                {
                    case "users":
                        if (!int.TryParse(id, out var uid)) { errors.Add($"{id}: invalid id"); continue; }
                        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
                        if (user is null) { errors.Add($"{id}: not found"); continue; }
                        if (user.DeletedAt is null) continue;
                        user.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "User", user.Id, $"{user.Email} (bulk)");
                        break;
                    case "boards":
                        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == id);
                        if (board is null) { errors.Add($"{id}: not found"); continue; }
                        if (board.DeletedAt is null) continue;
                        board.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "Board", board.Id, $"{board.Title} (bulk)");
                        break;
                    case "problems":
                        var p = await _db.Problems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (p is null) { errors.Add($"{id}: not found"); continue; }
                        if (p.DeletedAt is null) continue;
                        p.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "Problem", p.Id, $"{p.Title} (bulk)");
                        break;
                    case "bank":
                        var bp = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (bp is null) { errors.Add($"{id}: not found"); continue; }
                        if (bp.DeletedAt is null) continue;
                        bp.DeletedAt = null;
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "restore", "BankProblem", bp.Id, $"{bp.Title} (bulk)");
                        break;
                }
                count++;
            }
            catch (Exception ex) { errors.Add($"{id}: {ex.Message}"); }
        }
        return new AdminTrashBulkResult(count, errors);
    }

    /// <summary>Bulk permanent purge, one trash category at a time.</summary>
    [HttpPost("trash/{kind}/purge")]
    public async Task<ActionResult<AdminTrashBulkResult>> BulkPurgeTrash(string kind, AdminTrashBulkDto dto)
    {
        if (!TrashKinds.Contains(kind)) return BadRequest("kind must be users, boards, problems, or bank.");

        int count = 0;
        var errors = new List<string>();
        foreach (var id in (dto.Ids ?? new()).Distinct())
        {
            try
            {
                switch (kind)
                {
                    case "users":
                        if (!int.TryParse(id, out var uid)) { errors.Add($"{id}: invalid id"); continue; }
                        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
                        if (user is null) { errors.Add($"{id}: not found"); continue; }
                        if (user.DeletedAt is null) { errors.Add($"{user.Email}: delete it first"); continue; }
                        if (await _db.Boards.IgnoreQueryFilters().AnyAsync(b => b.OwnerId == uid))
                        { errors.Add($"{user.Email}: still owns board(s)"); continue; }
                        _db.Users.Remove(user);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "User", uid, $"{user.Email} (bulk)");
                        break;
                    case "boards":
                        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == id);
                        if (board is null) { errors.Add($"{id}: not found"); continue; }
                        if (board.DeletedAt is null) { errors.Add($"{board.Title}: delete it first"); continue; }
                        _db.Boards.Remove(board);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Board", board.Id, $"{board.Title} (bulk)");
                        break;
                    case "problems":
                        var p = await _db.Problems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (p is null) { errors.Add($"{id}: not found"); continue; }
                        if (p.DeletedAt is null) { errors.Add($"{p.Title}: delete it first"); continue; }
                        _db.Problems.Remove(p);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Problem", p.Id, $"{p.Title} (bulk)");
                        break;
                    case "bank":
                        var bp = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == id);
                        if (bp is null) { errors.Add($"{id}: not found"); continue; }
                        if (bp.DeletedAt is null) { errors.Add($"{bp.Title}: delete it first"); continue; }
                        _db.BankProblems.Remove(bp);
                        await _db.SaveChangesAsync();
                        await _audit.RecordAsync(UserId, ActorEmail, "purge", "BankProblem", bp.Id, $"{bp.Title} (bulk)");
                        break;
                }
                count++;
            }
            catch (Exception ex) { errors.Add($"{id}: {ex.Message}"); }
        }
        return new AdminTrashBulkResult(count, errors);
    }

    [HttpDelete("trash/users/{id:int}")]
    public async Task<IActionResult> PurgeUser(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");
        if (await _db.Boards.IgnoreQueryFilters().AnyAsync(b => b.OwnerId == id))
            return Conflict("This user still owns board(s) — delete/purge those first.");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "User", id, user.Email);
        return NoContent();
    }

    [HttpDelete("trash/boards/{slug}")]
    public async Task<IActionResult> PurgeBoard(string slug)
    {
        var board = await _db.Boards.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return NotFound();
        if (board.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Board", board.Id, board.Title);
        return NoContent();
    }

    [HttpDelete("trash/problems/{slug}")]
    public async Task<IActionResult> PurgeProblem(string slug)
    {
        var p = await _db.Problems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == slug);
        if (p is null) return NotFound();
        if (p.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");

        _db.Problems.Remove(p);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "Problem", p.Id, p.Title);
        return NoContent();
    }

    [HttpDelete("trash/bank/{slug}")]
    public async Task<IActionResult> PurgeBankProblem(string slug)
    {
        var b = await _db.BankProblems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        if (b.DeletedAt is null) return BadRequest("Delete it first — purge only removes trashed rows.");

        _db.BankProblems.Remove(b);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(UserId, ActorEmail, "purge", "BankProblem", b.Id, b.Title);
        return NoContent();
    }

}
