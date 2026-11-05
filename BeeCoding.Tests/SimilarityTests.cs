using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Similarity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SimilarityTests
{
    private const string Shortest = "Diberikan sebuah kalimat yang kata-katanya dipisahkan satu atau lebih spasi. Cetak kata yang paling pendek. Jika ada beberapa kata dengan panjang sama, cetak yang paling awal muncul. Batasan: panjang kalimat <= 1000.";
    private const string ShortestCopy = "Diberikan sebuah kalimat yang kata-katanya dipisahkan satu atau lebih spasi. Cetak kata yang paling pendek; bila ada beberapa kata sama pendeknya, cetak yang paling awal muncul. Batasan: panjang kalimat <= 5000.";
    private const string Primes = "Uraikan bilangan N menjadi perkalian faktor prima. Tampilkan setiap faktor prima berbeda dalam urutan menaik dalam bentuk p^e.";
    private const string Longest = "Diberikan sebuah kalimat yang kata-katanya dipisahkan spasi. Cetak kata yang paling panjang. Jika ada beberapa kata sama panjang, cetak yang paling awal muncul.";

    // Where a model is installed, run the embedding tests against it: E5_MODEL_DIR=<folder with model_quantized.onnx + sentencepiece.bpe.model>.
    private static readonly string? ModelDir = Environment.GetEnvironmentVariable("E5_MODEL_DIR") is { Length: > 0 } d && File.Exists(Path.Combine(d, "model_quantized.onnx")) ? d : null;

    [Fact]
    public void Text_overlap_ranks_a_rewrite_above_a_same_topic_problem_and_an_unrelated_one()
    {
        var s = TextSimilarity.Scores("Kata Terpendek\n" + Shortest, ["Kata Terpendek\n" + ShortestCopy, "Kata Terpanjang\n" + Longest, "Faktorisasi Prima\n" + Primes]);
        Assert.True(s[0] > s[1] && s[1] > s[2], string.Join(", ", s));
        Assert.Equal(0, TextSimilarity.Scores("", ["abc"])[0]);   // nothing to compare is not an error
    }

    [Fact]
    public void Sentencepiece_ids_become_the_ids_the_xlmr_model_expects()
    {
        // verified against the Hugging Face tokenizer: <s> dropped and re-added as 0, pieces shifted by one, </s> = 2 appended
        Assert.Equal([0L, 41, 1294, 2], EmbeddingService.ToModelIds([1, 40, 1293]));
        Assert.Equal(512, EmbeddingService.ToModelIds(Enumerable.Range(1, 2000).ToList()).Length);
    }

    private static async Task<(TestApp app, HttpClient admin, int targetId, int copyId)> SeedAsync()
    {
        var app = new TestApp();
        int targetId, copyId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            BankProblem Make(string title, string text, bool pub) => new() { OwnerId = owner.Id, Title = title, StatementMarkdown = text, IsPublic = pub, PendingReview = !pub };
            var target = Make("Kata Terpendek", Shortest, false);
            var copy = Make("Kata Tersingkat", ShortestCopy, true);
            db.BankProblems.AddRange(target, copy, Make("Kata Terpanjang", Longest, true), Make("Faktorisasi Prima", Primes, true));
            await db.SaveChangesAsync();
            targetId = target.Id; copyId = copy.Id;
        }
        var admin = app.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        return (app, admin, targetId, copyId);
    }

    [Fact]
    public async Task Similar_problems_lists_the_nearest_first_and_only_to_admins()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            var (app, admin, targetId, copyId) = await SeedAsync();
            using var _ = app;
            var r = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin-ui/bank/{targetId}/similar")).RootElement;
            Assert.Equal("text", r.GetProperty("method").GetString());
            Assert.Equal(copyId, r.GetProperty("items")[0].GetProperty("id").GetInt32());
            Assert.DoesNotContain(r.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == targetId);   // never itself

            var student = app.CreateClient();
            (await student.PostAsJsonAsync("/api/auth/register", new { email = "s@test.dev", password = "Passw0rd!x1", displayName = "Siswa", role = "Student" })).EnsureSuccessStatusCode();
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await student.GetAsync($"/api/admin-ui/bank/{targetId}/similar")).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public void The_embedding_matches_the_reference_implementation_and_ranks_by_meaning()
    {
        if (ModelDir is null) return;
        using var svc = new EmbeddingService(Microsoft.Extensions.Options.Options.Create(new EmbeddingOptions { ModelDir = ModelDir }), Microsoft.Extensions.Logging.Abstractions.NullLogger<EmbeddingService>.Instance);
        var a = svc.Embed("Kata Terpendek\nDiberikan sebuah kalimat, cetak kata paling pendek. N <= 1000");
        // first six components from the Hugging Face / transformers.js pipeline on the same text
        float[] expected = [0.025f, -0.0263f, -0.0495f, -0.0458f, 0.0458f, -0.0475f];
        for (int i = 0; i < 6; i++) Assert.InRange(a[i], expected[i] - 0.01f, expected[i] + 0.01f);
        Assert.InRange(EmbeddingService.Dot(a, a), 0.999, 1.001);
        var paraphrase = svc.Embed("Teks Paling Ringkas\nTemukan kata dengan jumlah karakter paling sedikit.");
        var unrelated = svc.Embed("Faktorisasi Prima\nUraikan bilangan N menjadi faktor prima.");
        Assert.True(EmbeddingService.Dot(a, paraphrase) > EmbeddingService.Dot(a, unrelated));
    }

    [Fact]
    public async Task The_backfill_fills_in_embeddings_and_the_endpoint_then_ranks_with_them()
    {
        if (ModelDir is null) return;
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        Environment.SetEnvironmentVariable("Embeddings__ModelDir", ModelDir);
        try
        {
            var (app, admin, targetId, copyId) = await SeedAsync();
            using var _ = app;
            var first = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin-ui/bank/{targetId}/similar")).RootElement;
            Assert.Equal("text", first.GetProperty("method").GetString());   // nothing embedded yet: falls back instead of hiding problems

            var backfill = app.Services.GetServices<IHostedService>().OfType<EmbeddingBackfill>().Single();
            int candidates;
            using (var scope = app.Services.CreateScope())
                candidates = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BankProblems.CountAsync(b => b.IsPublic || b.PendingReview);
            Assert.Equal(candidates, await backfill.SweepAsync(CancellationToken.None));   // the seeded bank plus ours, all in one pass
            Assert.Equal(0, await backfill.SweepAsync(CancellationToken.None));   // nothing changed: nothing to redo

            var r = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin-ui/bank/{targetId}/similar")).RootElement;
            Assert.Equal("e5", r.GetProperty("method").GetString());
            Assert.Equal(copyId, r.GetProperty("items")[0].GetProperty("id").GetInt32());
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); Environment.SetEnvironmentVariable("Embeddings__ModelDir", null); }
    }
}
