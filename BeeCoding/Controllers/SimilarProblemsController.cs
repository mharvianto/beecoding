using BeeCoding.Data;
using BeeCoding.Services.Similarity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Admin-only review aid: the bank problems (public or awaiting review, anyone's) closest to one problem, to spot a duplicate
/// before approving it. Authors get the narrower version in <see cref="BankController"/>. Only a list for a human — scores are
/// not a verdict. Needs no AI provider, so the AI kill switch does not apply.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui/bank/{id:int}/similar")]
public class SimilarProblemsController(AppDbContext db, SimilarProblemFinder finder) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SimilarProblemsDto>> Get(int id, [FromQuery] int take = 5)
    {
        var target = await db.BankProblems.Where(b => b.Id == id).Select(b => new { b.Title, b.StatementMarkdown }).FirstOrDefaultAsync();
        if (target is null) return NotFound();
        var others = (await db.BankProblems.Where(b => b.Id != id && (b.IsPublic || b.PendingReview))
            .Select(b => new { b.Id, b.Slug, b.Title, b.StatementMarkdown, b.IsPublic, b.OwnerId }).ToListAsync())
            .Select(b => new SimilarProblemFinder.Candidate(b.Id, b.Slug, b.Title, b.StatementMarkdown, b.IsPublic, b.OwnerId)).ToList();
        return await finder.FindAsync(target.Title, target.StatementMarkdown, others, UserId, Math.Clamp(take, 1, 20));
    }
}
