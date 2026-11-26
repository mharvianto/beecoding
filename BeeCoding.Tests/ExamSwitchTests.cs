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
public class ExamSwitchTests
{
    [Fact]
    public async Task The_three_switches_are_independent_and_no_AI_hints_also_closes_practice_and_live_code()
    {
        var env = new Dictionary<string, string> { ["Ai__Enabled"] = "true", ["Ai__ApiKey"] = "test", ["Ai__BaseUrl"] = "http://127.0.0.1:9/v1/chat/completions", ["Ai__RateLimitSeconds"] = "1", ["Ai__TimeoutSeconds"] = "2" };
        foreach (var (k, v) in env) Environment.SetEnvironmentVariable(k, v);
        try
        {
            using var app = new TestApp();
            var student = app.CreateClient();
            (await student.PostAsJsonAsync("/api/auth/register", new { email = "ex@t.test", password = "password1!", displayName = "Exam Student", role = "Student" })).EnsureSuccessStatusCode();
            (await student.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
            var teacher = app.CreateClient();
            (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

            int problemId, bankId; string slug;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var p = await db.Problems.FirstAsync();
                var bank = await db.BankProblems.FirstAsync(b => b.Id == p.BankProblemId);
                bank.IsPublic = true;   // the same problem is also in Practice
                await db.SaveChangesAsync();
                (problemId, bankId, slug) = (p.Id, bank.Id, p.Slug);
            }
            async Task<HttpStatusCode> Hint(HttpClient who, object about)
            {
                await Task.Delay(1100);   // the spacing between two hints
                var body = JsonSerializer.SerializeToNode(about)!.AsObject();
                body["code"] = "int main(){}"; body["language"] = "c";
                return (await who.PostAsJsonAsync("/api/ai/hint", body)).StatusCode;
            }
            (await teacher.PatchAsJsonAsync("/api/boards/demo-board", new { lecturingMode = true })).EnsureSuccessStatusCode();
            Assert.NotEqual(HttpStatusCode.Forbidden, await Hint(student, new { problemId }));

            // only paste blocked: nothing else follows
            (await teacher.PatchAsJsonAsync("/api/boards/demo-board", new { blockPaste = true })).EnsureSuccessStatusCode();
            var dto = await student.GetFromJsonAsync<JsonElement>($"/api/boards/demo-board/problems/{slug}");
            Assert.Equal((true, false, false), (dto.GetProperty("blockPaste").GetBoolean(), dto.GetProperty("examMode").GetBoolean(), dto.GetProperty("noAiHints").GetBoolean()));
            Assert.NotEqual(HttpStatusCode.Forbidden, await Hint(student, new { problemId }));

            (await teacher.PatchAsJsonAsync("/api/boards/demo-board", new { noAiHints = true })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, await Hint(student, new { problemId }));
            Assert.Equal(HttpStatusCode.Forbidden, await Hint(student, new { bankProblemId = bankId }));   // Practice
            Assert.Equal(HttpStatusCode.Forbidden, await Hint(student, new { boardSlug = "demo-board" }));  // Live Code
            Assert.NotEqual(HttpStatusCode.Forbidden, await Hint(teacher, new { problemId }));

            // a group's own switches win over the board's; once closed, the side doors open again but the problem stays without hints
            var g = await teacher.PostAsJsonAsync("/api/boards/demo-board/groups", new { title = "Quiz", hidden = false, examMode = false, noAiHints = true });
            g.EnsureSuccessStatusCode();
            var gid = (await g.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (await db.Problems.FirstAsync(p => p.Id == problemId)).GroupId = gid;
                (await db.ProblemGroups.FirstAsync(x => x.Id == gid)).ClosesAt = DateTime.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden, await Hint(student, new { problemId }));
            Assert.NotEqual(HttpStatusCode.Forbidden, await Hint(student, new { bankProblemId = bankId }));
            Assert.NotEqual(HttpStatusCode.Forbidden, await Hint(student, new { boardSlug = "demo-board" }));
        }
        finally { foreach (var k in env.Keys) Environment.SetEnvironmentVariable(k, null); }
    }
}
