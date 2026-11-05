using BeeCoding.Data;
using BeeCoding.Services.Similarity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

public record SimilarProblemDto(int Id, string Slug, string Title, double Score, string State);
public record SimilarProblemsDto(string Method, List<SimilarProblemDto> Items, int Candidates);

/// <summary>
/// Admin-only review aid: the bank problems (public or awaiting review) closest to one problem, to spot a duplicate before
/// approving it. Ranked by e5 embeddings when they are set up and current for every candidate, otherwise by text overlap.
/// Only a list for a human — scores are not a verdict. Needs no AI provider, so the AI kill switch does not apply.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui/bank/{id:int}/similar")]
public class SimilarProblemsController(AppDbContext db, EmbeddingService embeddings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SimilarProblemsDto>> Get(int id, [FromQuery] int take = 5)
    {
        var target = await db.BankProblems.Where(b => b.Id == id).Select(b => new { b.Title, b.StatementMarkdown }).FirstOrDefaultAsync();
        if (target is null) return NotFound();
        take = Math.Clamp(take, 1, 20);

        var others = await db.BankProblems.Where(b => b.Id != id && (b.IsPublic || b.PendingReview))
            .Select(b => new { b.Id, b.Slug, b.Title, b.StatementMarkdown, b.IsPublic }).ToListAsync();
        if (others.Count == 0) return new SimilarProblemsDto("none", [], 0);
        var targetText = BankProblemText(target.Title, target.StatementMarkdown);

        double[]? scores = null;
        var method = "text";
        if (embeddings.Enabled)
        {
            var stored = await db.BankProblemEmbeddings.ToDictionaryAsync(e => e.BankProblemId);
            var current = others.All(o => stored.TryGetValue(o.Id, out var e) && e.TextHash == EmbeddingService.HashOf(BankProblemText(o.Title, o.StatementMarkdown)));
            if (current)   // a half-filled set would hide the problems not embedded yet, so wait for the backfill
            {
                var v = await Task.Run(() => embeddings.Embed(targetText));
                scores = others.Select(o => EmbeddingService.Dot(v, EmbeddingService.FromBytes(stored[o.Id].Vector))).ToArray();
                method = "e5";
            }
        }
        scores ??= TextSimilarity.Scores(targetText, others.Select(o => BankProblemText(o.Title, o.StatementMarkdown)).ToList());

        var items = others.Select((o, i) => new SimilarProblemDto(o.Id, o.Slug, o.Title, Math.Round(scores[i], 3), o.IsPublic ? "public" : "pending"))
            .OrderByDescending(x => x.Score).Take(take).ToList();
        return new SimilarProblemsDto(method, items, others.Count);
    }

    private static string BankProblemText(string title, string statement) => EmbeddingBackfill.TextOf(title, statement);
}
