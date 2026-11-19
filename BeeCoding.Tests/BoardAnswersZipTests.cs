using System.IO.Compression;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class BoardAnswersZipTests
{
    [Fact]
    public async Task Answers_zip_pick_all_has_one_file_per_attempt_named_by_order_and_verdict_and_pick_best_still_one_per_student()
    {
        using var app = new TestApp();
        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Budi", role = "Student" })).EnsureSuccessStatusCode();
        string slug;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.Include(b => b.Problems).FirstAsync(b => b.Slug == "demo-board");
            var student = await db.Users.FirstAsync(u => u.Email == "kid@test.dev");
            db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = student.Id, Role = MembershipRole.Student });
            var p = board.Problems.OrderBy(x => x.Position).First();
            var t0 = DateTime.UtcNow.AddHours(-1);
            Submission Sub(Verdict v, double score, int min, string code) => new()
            {
                ProblemId = p.Id, UserId = student.Id, Language = "cpp", Code = code, Status = SubmissionStatus.Done, Verdict = v, Score = score, CreatedAt = t0.AddMinutes(min),
            };
            db.Submissions.AddRange(Sub(Verdict.CompileError, 0, 0, "// one"), Sub(Verdict.WrongAnswer, 0.4, 5, "// two"), Sub(Verdict.Accepted, 1.0, 9, "// three"));
            await db.SaveChangesAsync();
            slug = board.Slug;
        }

        static async Task<Dictionary<string, string>> Files(HttpClient c, string url)
        {
            var res = await c.GetAsync(url);
            res.EnsureSuccessStatusCode();
            using var zip = new ZipArchive(await res.Content.ReadAsStreamAsync());
            return zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
        }

        var all = await Files(teacher, $"/api/boards/{slug}/export/answers.zip?pick=all");
        var mine = all.Where(kv => kv.Key.Contains("Budi")).OrderBy(kv => kv.Key).ToList();
        Assert.Equal(3, mine.Count);
        Assert.EndsWith("/01-CE.cpp", mine[0].Key); Assert.Equal("// one", mine[0].Value);
        Assert.EndsWith("/02-WA.cpp", mine[1].Key); Assert.Equal("// two", mine[1].Value);
        Assert.EndsWith("/03-AC.cpp", mine[2].Key); Assert.Equal("// three", mine[2].Value);

        var best = await Files(teacher, $"/api/boards/{slug}/export/answers.zip?pick=best");
        var bestMine = best.Where(kv => kv.Key.Contains("Budi")).ToList();
        Assert.Single(bestMine);
        Assert.Equal("// three", bestMine[0].Value);
    }
}
