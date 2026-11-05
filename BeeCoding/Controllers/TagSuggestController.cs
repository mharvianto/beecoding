using BeeCoding.Data;
using BeeCoding.Services.Similarity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

public record TagSuggestDto(string? Title, string? Statement);

/// <summary>
/// Tag suggestions for the problem editor: the tags of the public problems whose meaning is closest to the text being written
/// (a vote over the 5 nearest, nearer ones counting more). Measured on this bank by leaving each problem out in turn: the top
/// tag was right 65% of the time (guessing the commonest tag: 25%), so these are chips to click, never applied on their own.
/// Public problems only, so no one's draft shapes what another author is offered. Needs the e5 model; without it, nothing.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tag-suggestions")]
public class TagSuggestController(AppDbContext db, EmbeddingService embeddings, SimilarCooldown cooldown) : ApiControllerBase
{
    private const int Neighbours = 5, MaxTags = 3;

    [HttpGet("enabled")]
    public object Enabled() => new { enabled = embeddings.Enabled };

    [HttpPost]
    public async Task<ActionResult<List<string>>> Suggest(TagSuggestDto dto)
    {
        var title = (dto.Title ?? "").Trim();
        var statement = (dto.Statement ?? "").Trim();
        // Too little text (or the untouched starting template) says nothing about the topic.
        if (!embeddings.Enabled || title.Length < 3 || statement.Length < 40) return new List<string>();
        if (!cooldown.TryAcquire(UserId, "tags", TimeSpan.FromSeconds(2))) return StatusCode(StatusCodes.Status429TooManyRequests, "Wait a moment before asking again.");

        var tagged = await db.BankProblems.Where(b => b.IsPublic && b.Tags != "").Select(b => new { b.Id, b.Tags }).ToListAsync();
        var ids = tagged.Select(t => t.Id).ToList();
        var stored = await db.BankProblemEmbeddings.Where(e => ids.Contains(e.BankProblemId)).ToDictionaryAsync(e => e.BankProblemId, e => e.Vector);
        var v = await Task.Run(() => embeddings.Embed(EmbeddingBackfill.TextOf(title, statement)));

        var vote = new Dictionary<string, double>();
        foreach (var n in tagged.Where(t => stored.ContainsKey(t.Id))
                     .Select(t => (t.Tags, Score: EmbeddingService.Dot(v, EmbeddingService.FromBytes(stored[t.Id]))))
                     .OrderByDescending(x => x.Score).Take(Neighbours))
            foreach (var tag in n.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                vote[tag] = vote.GetValueOrDefault(tag) + Math.Exp(20 * n.Score);   // e5 scores crowd together; the exponent lets the nearest ones decide
        return vote.OrderByDescending(x => x.Value).Take(MaxTags).Select(x => x.Key).ToList();
    }
}
