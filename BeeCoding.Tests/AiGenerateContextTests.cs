using System.Net;
using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Ai;
using BeeCoding.Controllers;
using BeeCoding.Services.Similarity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AiGenerateContextTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Body = await r.Content!.ReadAsStringAsync(ct);
            var problem = JsonSerializer.Serialize(new
            {
                title = "T", statementMarkdown = "s", tags = "x", level = "Easy", language = "cpp", starterCode = "", referenceSolution = "int main(){}",
                timeLimitMs = 1000, memoryLimitKb = 65536, tests = new[] { new { stdin = "1", isSample = true }, new { stdin = "2", isSample = false } },
            });
            var chunk = JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = problem }, finish_reason = "stop" } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"data: {chunk}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }

    [Fact]
    public async Task The_generator_is_told_about_the_closest_public_problems_and_never_about_private_ones()
    {
        using var app = new TestApp();
        List<string> near;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.FirstAsync();
            db.BankProblems.AddRange(
                new BankProblem { OwnerId = owner.Id, Title = "Jumlah Dua Angka", StatementMarkdown = "Baca dua bilangan lalu cetak jumlah dua bilangan itu.", Tags = "math", IsPublic = true, Level = ProblemLevel.Easy },
                new BankProblem { OwnerId = owner.Id, Title = "Draf Rahasia Jumlah Bilangan", StatementMarkdown = "Baca dua bilangan lalu cetak jumlah dua bilangan.", Tags = "secret", IsPublic = false, Level = ProblemLevel.Easy });
            await db.SaveChangesAsync();
            near = await AiController.NearestPublicAsync(db, scope.ServiceProvider.GetRequiredService<SimilarProblemFinder>(), "soal menjumlahkan dua bilangan");
        }
        Assert.Contains("Jumlah Dua Angka [math]", near);
        Assert.DoesNotContain(near, n => n.Contains("Rahasia"));

        var cap = new Capture();
        var svc = new AiTutorService(new HttpClient(cap), Microsoft.Extensions.Options.Options.Create(new AiOptions { ApiKey = "k", BaseUrl = "https://x.example/v1" }),
            new AiRuntimeSettings(), new AiProviderRuntime(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AiTutorService>.Instance);
        await svc.GenerateProblemAsync("soal menjumlahkan dua bilangan", "Easy", "cpp", 5, "id", default, near);
        Assert.Contains("Jumlah Dua Angka [math]", cap.Body);
        Assert.Contains("Do NOT repeat any of them", cap.Body);
        Assert.DoesNotContain("Rahasia", cap.Body);
    }
}
