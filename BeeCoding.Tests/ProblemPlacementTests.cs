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
public class ProblemPlacementTests
{
    [Fact]
    public async Task A_problem_created_into_a_draft_group_or_as_a_draft_is_never_visible_to_students_and_announces_nothing()
    {
        using var app = new TestApp();
        var owner = app.CreateClient();
        (await owner.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "place@t.test", password = "password1!", displayName = "Placed Kid", role = "Student" })).EnsureSuccessStatusCode();
        (await kid.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
        var g = await owner.PostAsJsonAsync("/api/boards/demo-board/groups", new { title = "Exam", hidden = true, examMode = false });
        var gid = (await g.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        object Body(string title, int? groupId, bool? hidden) => new
        {
            title, statementMarkdown = "Print 1.", allowedLanguages = "c", timeLimitMs = 1000, memoryLimitKb = 32768, position = 0, tags = "", level = "Easy",
            testCases = new[] { new { stdin = "", expectedStdout = "1", isSample = true, points = 1, position = 0 } }, groupId, hidden,
        };
        (await owner.PostAsJsonAsync("/api/boards/demo-board/problems", Body("In exam group", gid, false))).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/boards/demo-board/problems", Body("Loose draft", null, true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/boards/demo-board/problems", Body("Nowhere", 999999, false))).StatusCode);

        var mine = await owner.PostAsJsonAsync("/api/bank", Body("From the bank", null, null));
        Assert.True(mine.IsSuccessStatusCode, await mine.Content.ReadAsStringAsync());
        var bankId = (await mine.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsync($"/api/bank/{bankId}/copy-to/demo-board?groupId={gid}&hidden=false", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/bank/{bankId}/copy-to/demo-board?groupId=999999", null)).StatusCode);

        var seen = await kid.GetFromJsonAsync<JsonElement>("/api/boards/demo-board/problems");
        Assert.Single(seen.EnumerateArray());   // only the seeded problem
        using var s = app.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Notifications.AnyAsync(n => n.Kind == "problem-new"));
        Assert.Equal(2, await db.Problems.CountAsync(p => p.GroupId == gid));
    }
}
