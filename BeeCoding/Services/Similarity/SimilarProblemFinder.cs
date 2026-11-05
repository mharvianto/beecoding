using System.Collections.Concurrent;
using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services.Similarity;

/// <param name="Linkable">Whether the viewer may open it at /practice/{slug} (public, or their own) — the list never carries more than that.</param>
public record SimilarProblemDto(int Id, string Slug, string Title, double Score, string State, bool Linkable);
public record SimilarProblemsDto(string Method, List<SimilarProblemDto> Items, int Candidates);

/// <summary>Minimum spacing between a user's requests of one kind (an author check, a search suggestion): embedding is CPU work a whole class could trigger at once.</summary>
public sealed class SimilarCooldown
{
    private readonly ConcurrentDictionary<(int, string), long> _last = new();

    public bool TryAcquire(int userId, string kind, TimeSpan gap)
    {
        var now = DateTime.UtcNow.Ticks;
        lock (_last)
        {
            if (_last.TryGetValue((userId, kind), out var prev) && now - prev < gap.Ticks) return false;
            _last[(userId, kind)] = now;
            return true;
        }
    }
}

/// <summary>
/// Ranks candidate bank problems by closeness to a text: e5 embeddings when they are set up and current for EVERY candidate
/// (a half-filled set would hide the rest), otherwise text overlap. The caller decides who the candidates are — that is where
/// access is enforced (a viewer must only be offered problems they may already see); this only ranks what it is given.
/// </summary>
public sealed class SimilarProblemFinder(AppDbContext db, EmbeddingService embeddings, TextCorpusCache corpora)
{
    public sealed record Candidate(int Id, string Slug, string Title, string StatementMarkdown, bool IsPublic, int OwnerId);

    public async Task<SimilarProblemsDto> FindAsync(string title, string statement, IReadOnlyList<Candidate> others, int viewerId, int take)
    {
        if (others.Count == 0) return new("none", [], 0);
        var text = EmbeddingBackfill.TextOf(title, statement);
        string TextOf(Candidate o) => EmbeddingBackfill.TextOf(o.Title, o.StatementMarkdown);

        double[]? scores = null;
        var method = "text";
        if (embeddings.Enabled)
        {
            var ids = others.Select(o => o.Id).ToList();
            var stored = await db.BankProblemEmbeddings.Where(e => ids.Contains(e.BankProblemId)).ToDictionaryAsync(e => e.BankProblemId);
            if (others.All(o => stored.TryGetValue(o.Id, out var e) && e.TextHash == EmbeddingService.HashOf(TextOf(o))))
            {
                var v = await Task.Run(() => embeddings.Embed(text));
                scores = others.Select(o => EmbeddingService.Dot(v, EmbeddingService.FromBytes(stored[o.Id].Vector))).ToArray();
                method = "e5";
            }
        }
        if (scores is null)
        {
            var corpus = await corpora.GetAsync(async () => (await db.BankProblems.Where(b => b.IsPublic || b.PendingReview)
                .Select(b => new { b.Id, b.Title, b.StatementMarkdown }).ToListAsync()).Select(b => (b.Id, EmbeddingBackfill.TextOf(b.Title, b.StatementMarkdown))).ToList());
            scores = corpus.Scores(text, others.Select(o => o.Id).ToList());
        }

        var items = others.Select((o, i) => new SimilarProblemDto(o.Id, o.Slug, o.Title, Math.Round(scores[i], 3),
                o.IsPublic ? "public" : "pending", o.IsPublic || o.OwnerId == viewerId))
            .OrderByDescending(x => x.Score).Take(take).ToList();
        return new(method, items, others.Count);
    }
}
