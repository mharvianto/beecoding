using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class BoardProblemBankTests
{
    static UpsertProblemDto Dto(string title, string statement, string expected = "5\n") =>
        new(title, statement, "", 1000, 32768, 0, "", "Easy",
            [new UpsertTestCaseDto(null, "2 3\n", expected, true, 1, 0)]);

    static async Task<HttpClient> TeacherAsync(TestApp app)
    {
        var c = app.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task A_problem_made_on_a_board_gets_a_private_bank_row_kept_out_of_the_bank_list_and_edits_write_through()
    {
        using var app = new TestApp();
        var t = await TeacherAsync(app);
        var board = (await t.GetFromJsonAsync<JsonElement>("/api/boards")).EnumerateArray().First();
        var slug = board.GetProperty("slug").GetString()!;

        var made = await t.PostAsJsonAsync($"/api/boards/{slug}/problems", Dto("Sum", "Add two numbers"));
        made.EnsureSuccessStatusCode();
        var pSlug = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;

        (await t.PutAsJsonAsync($"/api/boards/{slug}/problems/{pSlug}", Dto("Sum", "Add two integers"))).EnsureSuccessStatusCode();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Problems.FirstAsync(x => x.Slug == pSlug);
        var bank = await db.BankProblems.Include(b => b.TestCases).FirstAsync(b => b.Id == p.BankProblemId);
        Assert.True(bank.BoardOnly);
        Assert.False(bank.IsPublic);
        Assert.Equal("Add two integers", bank.StatementMarkdown);
        Assert.Single(bank.TestCases);

        var list = await t.GetFromJsonAsync<JsonElement>("/api/bank");
        Assert.DoesNotContain(list.EnumerateArray(), b => b.GetProperty("slug").GetString() == bank.Slug);
    }

    [Fact]
    public async Task After_a_submission_the_graded_content_is_locked_but_the_title_is_not_and_a_new_version_is_a_hidden_draft()
    {
        using var app = new TestApp();
        var t = await TeacherAsync(app);
        var slug = (await t.GetFromJsonAsync<JsonElement>("/api/boards")).EnumerateArray().First().GetProperty("slug").GetString()!;
        var made = await t.PostAsJsonAsync($"/api/boards/{slug}/problems", Dto("Sum", "Add two numbers"));
        var pSlug = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Problems.FirstAsync(x => x.Slug == pSlug);
            var teacherId = (await db.Users.FirstAsync(u => u.Email == "teacher@demo.test")).Id;
            db.Submissions.Add(new Submission { ProblemId = p.Id, UserId = teacherId, Code = "x" });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await t.PutAsJsonAsync($"/api/boards/{slug}/problems/{pSlug}", Dto("Sum", "Add two numbers", expected: "6\n"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.PutAsJsonAsync($"/api/boards/{slug}/problems/{pSlug}", Dto("Sum", "Different text"))).StatusCode);
        (await t.PutAsJsonAsync($"/api/boards/{slug}/problems/{pSlug}", Dto("Sum, renamed", "Add two numbers"))).EnsureSuccessStatusCode();

        var v2 = await t.PostAsync($"/api/boards/{slug}/problems/{pSlug}/new-version", null);
        v2.EnsureSuccessStatusCode();
        var v2Slug = (await v2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;
        (await t.PutAsJsonAsync($"/api/boards/{slug}/problems/{v2Slug}", Dto("Sum v2", "Different text", expected: "6\n"))).EnsureSuccessStatusCode();

        using var scope2 = app.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var copy = await db2.Problems.FirstAsync(x => x.Slug == v2Slug);
        var orig = await db2.Problems.FirstAsync(x => x.Slug == pSlug);
        Assert.True(copy.Hidden);
        Assert.NotEqual(orig.BankProblemId, copy.BankProblemId);
        Assert.Equal("Add two numbers", (await db2.BankProblems.FirstAsync(b => b.Id == orig.BankProblemId)).StatementMarkdown);
    }

    [Fact]
    public async Task Startup_links_old_board_problems_to_a_bank_row_and_repeating_it_adds_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"beecoding-test-{Guid.NewGuid():N}.db");
        try
        {
            int pid;
            using (var app = new TestApp(path))
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
                var old = new Problem { BoardId = board.Id, Title = "Old", StatementMarkdown = "x" };   // as if made before this feature
                db.Problems.Add(old);
                await db.SaveChangesAsync();
                pid = old.Id;
            }
            int bankCount;
            for (var run = 0; run < 2; run++)
            {
                using var app = new TestApp(path);
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.NotNull((await db.Problems.FirstAsync(x => x.Id == pid)).BankProblemId);
                Assert.Equal(0, await db.Problems.CountAsync(x => x.BankProblemId == null));
                bankCount = await db.BankProblems.CountAsync(b => b.BoardOnly);
                if (run == 1) Assert.Equal(await db.Problems.CountAsync(), bankCount);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) try { File.Delete(f); } catch { }
        }
    }
}
