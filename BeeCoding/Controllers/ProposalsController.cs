using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// The board owner's side of "propose a problem" (the proposer's side lives in <see cref="BankController"/>).
/// Approving copies the bank problem onto the board — hidden, so the owner decides when students see it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/boards/{slug}/proposals")]
public class ProposalsController(AppDbContext db, IBoardNotifier notifier, NotificationService notifications) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly NotificationService _notifications = notifications;

    /// <summary>Pending proposals first, then the most recent decisions.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<BoardProposalDto>>> List(string slug)
    {
        var (board, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var rows = await _db.ProblemProposals
            .Where(x => x.BoardId == board!.Id && x.BankProblem != null && x.Status != ProposalStatus.Withdrawn)
            .Include(x => x.BankProblem).ThenInclude(b => b!.TestCases)
            .Include(x => x.Proposer)
            .OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync();

        return rows.Select(x => new BoardProposalDto(
            x.Id, x.BankProblem!.Slug, x.BankProblem.Title, x.BankProblem.Level.ToString(), x.BankProblem.Tags,
            x.BankProblem.StatementMarkdown, x.BankProblem.AllowedLanguages, x.Status.ToString(),
            x.Proposer?.DisplayName ?? "?", x.Note, x.ReviewNote, x.CreatedAt,
            x.BankProblem.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id).Select(t => Mapping.ToDto(t)).ToList())).ToList();
    }

    /// <summary>How many proposals are waiting — for the badge on the board page.</summary>
    [HttpGet("count")]
    public async Task<ActionResult<object>> Count(string slug)
    {
        var (board, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;
        return new { pending = await _db.ProblemProposals.CountAsync(x => x.BoardId == board!.Id && x.Status == ProposalStatus.Pending && x.BankProblem != null) };
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<ProblemDto>> Approve(string slug, int id)
    {
        var (board, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var proposal = await _db.ProblemProposals.Include(x => x.Proposer)
            .FirstOrDefaultAsync(x => x.Id == id && x.BoardId == board!.Id);
        if (proposal is null) return NotFound();
        if (proposal.Status != ProposalStatus.Pending) return Conflict("This proposal was already answered.");

        // The bank problem may have been deleted since (soft-delete filter hides it).
        var bank = await _db.BankProblems.Include(x => x.TestCases).FirstOrDefaultAsync(x => x.Id == proposal.BankProblemId);
        if (bank is null) return StatusCode(StatusCodes.Status410Gone, "The proposer deleted this problem.");

        int nextPos = await _db.Problems.Where(p => p.BoardId == board!.Id)
            .Select(p => (int?)p.Position).MaxAsync() is int max ? max + 1 : 0;
        var problem = BoardBulkService.ProblemFromBank(bank, board!.Id, nextPos);
        problem.Hidden = true;
        _db.Problems.Add(problem);

        proposal.Status = ProposalStatus.Approved;
        proposal.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifier.ProblemChangedAsync(board.Id);

        await _notifications.ProposalReviewedAsync(proposal, UserId, await ReviewerNameAsync(), bank.Title, "/bank");
        return Mapping.ToOwnerDto(problem);
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(string slug, int id, ProposalReviewDto dto)
    {
        var (board, err) = await RequireOwnerAsync(slug);
        if (err is not null) return err;

        var proposal = await _db.ProblemProposals.Include(x => x.BankProblem)
            .FirstOrDefaultAsync(x => x.Id == id && x.BoardId == board!.Id);
        if (proposal is null) return NotFound();
        if (proposal.Status != ProposalStatus.Pending) return Conflict("This proposal was already answered.");

        var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
        proposal.Status = ProposalStatus.Rejected;
        proposal.ReviewNote = reason is { Length: > 300 } ? reason[..300] : reason;
        proposal.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifications.ProposalReviewedAsync(proposal, UserId, await ReviewerNameAsync(),
            proposal.BankProblem?.Title ?? "problem", "/bank");
        return NoContent();
    }

    private Task<string> ReviewerNameAsync() =>
        _db.Users.Where(u => u.Id == UserId).Select(u => u.DisplayName).FirstAsync();

    private async Task<(Board? board, ActionResult? error)> RequireOwnerAsync(string slug)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return (null, NotFound());
        if (board.OwnerId != UserId) return (null, Forbid());
        return (board, null);
    }
}
