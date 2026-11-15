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

    [Fact]
    public async Task A_copy_that_fell_behind_its_bank_problem_is_listed_and_updated_in_place_or_as_a_hidden_draft_once_submitted_to()
    {
        using var app = new TestApp();
        var t = await TeacherAsync(app);
        var slug = (await t.GetFromJsonAsync<JsonElement>("/api/boards")).EnumerateArray().First().GetProperty("slug").GetString()!;
        int bankId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var b = new BankProblem { OwnerId = owner.Id, Title = "Bank sum", StatementMarkdown = "v1" };
            b.TestCases.Add(new BankTestCase { Stdin = "1\n", ExpectedStdout = "1\n", Points = 1 });
            db.BankProblems.Add(b);
            await db.SaveChangesAsync();
            bankId = b.Id;
        }
        string Slug(HttpResponseMessage r) => r.Content.ReadFromJsonAsync<JsonElement>().Result.GetProperty("slug").GetString()!;
        var plain = Slug(await t.PostAsync($"/api/bank/{bankId}/copy-to/{slug}", null));
        var used = Slug(await t.PostAsync($"/api/bank/{bankId}/copy-to/{slug}", null));
        Assert.Empty((await t.GetFromJsonAsync<JsonElement>($"/api/boards/{slug}/problems/bank-updates")).EnumerateArray());

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var b = await db.BankProblems.Include(x => x.TestCases).FirstAsync(x => x.Id == bankId);
            b.StatementMarkdown = "v2";
            b.TestCases.Single().ExpectedStdout = "2\n";
            var p = await db.Problems.FirstAsync(x => x.Slug == used);
            db.Submissions.Add(new Submission { ProblemId = p.Id, UserId = b.OwnerId, Code = "x" });
            await db.SaveChangesAsync();
        }

        var behind = (await t.GetFromJsonAsync<JsonElement>($"/api/boards/{slug}/problems/bank-updates")).EnumerateArray().ToList();
        Assert.Equal(2, behind.Count);
        Assert.False(behind.Single(x => x.GetProperty("slug").GetString() == plain).GetProperty("locked").GetBoolean());
        Assert.True(behind.Single(x => x.GetProperty("slug").GetString() == used).GetProperty("locked").GetBoolean());

        (await t.PostAsync($"/api/boards/{slug}/problems/{plain}/update-from-bank", null)).EnsureSuccessStatusCode();
        var draft = Slug(await t.PostAsync($"/api/boards/{slug}/problems/{used}/update-from-bank", null));

        using var scope2 = app.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db2.Problems.Include(x => x.TestCases).FirstAsync(x => x.Slug == plain);
        Assert.Equal("v2", updated.StatementMarkdown);
        Assert.Equal("2\n", updated.TestCases.Single().ExpectedStdout);
        var untouched = await db2.Problems.FirstAsync(x => x.Slug == used);
        Assert.Equal("v1", untouched.StatementMarkdown);
        var newDraft = await db2.Problems.Include(x => x.TestCases).FirstAsync(x => x.Slug == draft);
        Assert.True(newDraft.Hidden);
        Assert.Equal("v2", newDraft.StatementMarkdown);
        // only the still-behind, already-submitted original is listed now
        var after = (await t.GetFromJsonAsync<JsonElement>($"/api/boards/{slug}/problems/bank-updates")).EnumerateArray().Select(x => x.GetProperty("slug").GetString()).ToList();
        Assert.Equal([used], after);
    }

    [Fact]
    public async Task A_private_bank_problem_can_be_shared_read_only_with_a_teacher_and_unshared()
    {
        using var app = new TestApp();
        var owner = await TeacherAsync(app);
        var made = await owner.PostAsJsonAsync("/api/bank", new UpsertProblemDto("Mine", "secret", "", 1000, 32768, 0, "", "Easy",
            [new UpsertTestCaseDto(null, "1\n", "1\n", true, 1, 0), new UpsertTestCaseDto(null, "2\n", "2\n", false, 1, 1)]));
        made.EnsureSuccessStatusCode();
        var slug = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;

        // a second teacher (registered, then promoted in the database) and a student
        foreach (var (email, role) in new[] { ("t2@test.dev", "Student"), ("kid@test.dev", "Student") })
            (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x1", displayName = email, role })).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.FirstAsync(u => u.Email == "t2@test.dev")).Role = UserRole.Teacher;
            await db.SaveChangesAsync();
        }
        var t2 = app.CreateClient();
        (await t2.PostAsJsonAsync("/api/auth/login", new { email = "t2@test.dev", password = "Passw0rd!x1" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await t2.GetAsync($"/api/bank/{slug}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/bank/{slug}/shares", new { email = "kid@test.dev" })).StatusCode);    // students cannot receive
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/bank/{slug}/shares", new { email = "teacher@demo.test" })).StatusCode);
        (await owner.PostAsJsonAsync($"/api/bank/{slug}/shares", new { email = "T2@test.dev" })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/bank/{slug}/shares", new { email = "t2@test.dev" })).EnsureSuccessStatusCode();                              // repeating is harmless
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/bank/{slug}/shares")).EnumerateArray());

        var seen = await t2.GetFromJsonAsync<JsonElement>($"/api/bank/{slug}");
        Assert.False(seen.GetProperty("mine").GetBoolean());
        Assert.Single(seen.GetProperty("testCases").EnumerateArray());                                                                                  // samples only
        var shared = await t2.GetFromJsonAsync<JsonElement>("/api/bank?scope=shared");
        Assert.Single(shared.EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await t2.PutAsJsonAsync($"/api/bank/{slug}", new UpsertProblemDto("x", "y", "", 1000, 32768, 0, "", "Easy", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t2.GetAsync($"/api/bank/{slug}/shares")).StatusCode);                                             // only the owner manages shares

        var uid = (await owner.GetFromJsonAsync<JsonElement>($"/api/bank/{slug}/shares")).EnumerateArray().First().GetProperty("userId").GetInt32();
        (await owner.DeleteAsync($"/api/bank/{slug}/shares/{uid}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await t2.GetAsync($"/api/bank/{slug}")).StatusCode);
    }

    [Fact]
    public async Task The_janitor_removes_a_board_only_row_once_its_problem_is_purged_but_keeps_one_still_in_use()
    {
        using var app = new TestApp();
        var t = await TeacherAsync(app);
        var slug = (await t.GetFromJsonAsync<JsonElement>("/api/boards")).EnumerateArray().First().GetProperty("slug").GetString()!;
        var slugs = new List<string>();
        foreach (var title in new[] { "Gone", "Kept" })
            slugs.Add((await (await t.PostAsJsonAsync($"/api/boards/{slug}/problems", Dto(title, "x"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!);

        int goneBank, keptBank;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gone = await db.Problems.FirstAsync(x => x.Slug == slugs[0]);
            var kept = await db.Problems.FirstAsync(x => x.Slug == slugs[1]);
            goneBank = gone.BankProblemId!.Value; keptBank = kept.BankProblemId!.Value;
            kept.DeletedAt = DateTime.UtcNow;                  // soft-deleted can still be restored: its row stays
            db.Problems.Remove(gone);                          // purged
            await db.SaveChangesAsync();
        }

        var janitor = new BeeCoding.Services.BoardOnlyJanitor(app.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BeeCoding.Services.BoardOnlyJanitor>.Instance);
        Assert.Equal(1, await janitor.SweepAsync(default));

        using var scope2 = app.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db2.BankProblems.IgnoreQueryFilters().AnyAsync(b => b.Id == goneBank));
        Assert.True(await db2.BankProblems.IgnoreQueryFilters().AnyAsync(b => b.Id == keptBank));
    }

    [Fact]
    public async Task The_admin_export_leaves_out_board_only_rows()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var t = await TeacherAsync(app);
            var slug = (await t.GetFromJsonAsync<JsonElement>("/api/boards")).EnumerateArray().First().GetProperty("slug").GetString()!;
            (await t.PostAsJsonAsync($"/api/boards/{slug}/problems", Dto("Secret exam question", "x"))).EnsureSuccessStatusCode();

            var json = await t.GetStringAsync("/api/admin-ui/problems/export");
            Assert.DoesNotContain("Secret exam question", json);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task Import_and_the_scripted_ingest_never_touch_a_board_only_row_and_the_scripted_list_hides_it()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        Environment.SetEnvironmentVariable("Admin__Token", "test-token-0123456789abcdef0123456789");
        try
        {
            using var app = new TestApp();
            var t = await TeacherAsync(app);
            var slug = (await t.GetFromJsonAsync<JsonElement>("/api/boards")).EnumerateArray().First().GetProperty("slug").GetString()!;
            var pSlug = (await (await t.PostAsJsonAsync($"/api/boards/{slug}/problems", Dto("Sum", "Original text"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;

            var test = new[] { new { stdin = "1\n", expectedStdout = "1\n", isSample = false, points = 1 } };
            var imp = await t.PostAsJsonAsync("/api/admin-ui/problems/import", new
            {
                version = 1, exportedAt = DateTime.UtcNow,
                problems = new[] { new { ownerEmail = "teacher@demo.test", title = "Sum", statementMarkdown = "Imported", allowedLanguages = "c,cpp", level = "Easy", tags = "", timeLimitMs = 1000, memoryLimitKb = 32768, isPublic = true, generatedByAi = false,
                    tests = new[] { new { stdin = "1\n", expectedStdout = "1\n", isSample = false, points = 1, position = 0 } } } },
            });
            imp.EnsureSuccessStatusCode();

            var anon = app.CreateClient();
            anon.DefaultRequestHeaders.Add("X-Admin-Token", "test-token-0123456789abcdef0123456789");
            (await anon.PostAsJsonAsync("/api/admin/bank-problems", new { problems = new[] { new { title = "Sum", statementMarkdown = "Ingested", isPublic = true, tests = test } } })).EnsureSuccessStatusCode();

            var listed = await anon.GetStringAsync("/api/admin/bank-problems");

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Problems.FirstAsync(x => x.Slug == pSlug);
            var own = await db.BankProblems.FirstAsync(b => b.Id == p.BankProblemId);
            Assert.True(own.BoardOnly);
            Assert.False(own.IsPublic);
            Assert.Equal("Original text", own.StatementMarkdown);
            Assert.Equal(1, await db.BankProblems.CountAsync(b => !b.BoardOnly && b.Title == "Sum" && b.StatementMarkdown == "Ingested"));
            Assert.DoesNotContain("Original text", listed);
            Assert.Equal(HttpStatusCode.NotFound, (await anon.DeleteAsync($"/api/admin/bank-problems/{own.Id}")).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); Environment.SetEnvironmentVariable("Admin__Token", null); }
    }
}
