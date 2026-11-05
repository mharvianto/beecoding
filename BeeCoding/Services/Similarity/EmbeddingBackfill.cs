using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services.Similarity;

/// <summary>
/// Keeps <see cref="BeeCoding.Models.BankProblemEmbedding"/> current for the problems a new one is compared with (public or
/// awaiting review). Does nothing unless <c>Embeddings:ModelDir</c> is set. A pass embeds at most <see cref="PerPass"/>
/// problems, so a big first backfill spreads over a few passes instead of holding the CPU.
/// </summary>
public sealed class EmbeddingBackfill(IServiceScopeFactory scopes, EmbeddingService embeddings, IOptions<EmbeddingOptions> opt, ILogger<EmbeddingBackfill> log) : BackgroundService
{
    private const int PerPass = 200;

    /// <summary>Title + statement: what both rankings compare.</summary>
    public static string TextOf(string title, string statement) => title + "\n" + statement;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!embeddings.Enabled) return;
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        var every = TimeSpan.FromMinutes(Math.Max(1, opt.Value.BackfillMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { var n = await SweepAsync(stoppingToken); if (n > 0) log.LogInformation("Embedded {Count} problems", n); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "embedding backfill failed"); }
            try { await Task.Delay(every, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass; returns how many embeddings were written. Also drops rows of problems that are no longer candidates.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!embeddings.Enabled) return 0;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var problems = await db.BankProblems.Where(b => b.IsPublic || b.PendingReview)
            .Select(b => new { b.Id, b.Title, b.StatementMarkdown }).ToListAsync(ct);
        var have = await db.BankProblemEmbeddings.ToDictionaryAsync(e => e.BankProblemId, ct);

        var ids = problems.Select(p => p.Id).ToHashSet();
        var gone = have.Keys.Where(k => !ids.Contains(k)).ToList();
        if (gone.Count > 0) await db.BankProblemEmbeddings.Where(e => gone.Contains(e.BankProblemId)).ExecuteDeleteAsync(ct);

        int written = 0;
        foreach (var p in problems)
        {
            if (written >= PerPass) break;
            ct.ThrowIfCancellationRequested();
            var text = TextOf(p.Title, p.StatementMarkdown);
            var hash = EmbeddingService.HashOf(text);
            if (have.TryGetValue(p.Id, out var row))
            {
                if (row.TextHash == hash) continue;
                row.TextHash = hash; row.Vector = EmbeddingService.ToBytes(embeddings.Embed(text));
            }
            else db.BankProblemEmbeddings.Add(new() { BankProblemId = p.Id, TextHash = hash, Vector = EmbeddingService.ToBytes(embeddings.Embed(text)) });
            written++;
        }
        await db.SaveChangesAsync(ct);
        return written;
    }
}
