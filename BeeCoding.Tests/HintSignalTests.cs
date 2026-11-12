using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class HintSignalTests
{
    [Fact]
    public async Task Staff_see_how_deep_a_student_is_into_AI_hints_but_students_do_not_and_stale_rows_are_left_out()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "stuck@test.dev", password = "Passw0rd!x1", displayName = "Stuck", role = "Student" })).EnsureSuccessStatusCode();
        (await kid.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
        string slug; int fresh, stale, kidId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
            slug = board.Slug;
            var a = new Problem { BoardId = board.Id, Title = "Fresh", StatementMarkdown = "x" };
            var b = new Problem { BoardId = board.Id, Title = "Stale", StatementMarkdown = "x" };
            db.Problems.AddRange(a, b);
            await db.SaveChangesAsync();
            fresh = a.Id; stale = b.Id;
            kidId = (await db.Users.FirstAsync(u => u.Email == "stuck@test.dev")).Id;
            db.AiHintProgresses.AddRange(
                new AiHintProgress { UserId = kidId, ProblemKey = $"board:{fresh}", Count = 7, UpdatedAt = DateTime.UtcNow },                    // capped to level 4
                new AiHintProgress { UserId = kidId, ProblemKey = $"board:{stale}", Count = 3, UpdatedAt = DateTime.UtcNow.AddHours(-5) },      // tutor would reset it
                new AiHintProgress { UserId = kidId, ProblemKey = "bank:1", Count = 3, UpdatedAt = DateTime.UtcNow });                          // practice, not this board
            await db.SaveChangesAsync();
        }

        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        var asTeacher = await teacher.GetFromJsonAsync<JsonElement>($"/api/boards/{slug}/progress");
        var hints = asTeacher.GetProperty("hints").EnumerateArray().ToList();
        var h = Assert.Single(hints);
        Assert.Equal(kidId, h.GetProperty("userId").GetInt32());
        Assert.Equal(fresh, h.GetProperty("problemId").GetInt32());
        Assert.Equal(4, h.GetProperty("level").GetInt32());

        var asStudent = await kid.GetFromJsonAsync<JsonElement>($"/api/boards/{slug}/progress");
        Assert.Equal(JsonValueKind.Null, asStudent.GetProperty("hints").ValueKind);
    }
}
