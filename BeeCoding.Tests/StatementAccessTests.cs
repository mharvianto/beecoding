using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class StatementAccessTests
{
    [Fact]
    public async Task A_student_cannot_read_the_statement_of_a_hidden_or_not_yet_open_problem_but_staff_can()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "probe@test.dev", password = "Passw0rd!x1", displayName = "Probe", role = "Student" })).EnsureSuccessStatusCode();
        (await kid.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
        int openId, hiddenId, notOpenId, hiddenGroupId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
            var tomorrow = new ProblemGroup { BoardId = board.Id, Title = "Besok", OpensAt = DateTime.UtcNow.AddDays(1), Position = 8 };
            var secretGroup = new ProblemGroup { BoardId = board.Id, Title = "Rahasia", Hidden = true, Position = 9 };
            db.ProblemGroups.AddRange(tomorrow, secretGroup);
            await db.SaveChangesAsync();
            var open = new Problem { BoardId = board.Id, Title = "Soal Terbuka", StatementMarkdown = "biasa" };
            var hidden = new Problem { BoardId = board.Id, Title = "Soal Disembunyikan", StatementMarkdown = "x", Hidden = true };
            var notOpen = new Problem { BoardId = board.Id, Title = "Soal Belum Dibuka", StatementMarkdown = "x", GroupId = tomorrow.Id };
            var inHiddenGroup = new Problem { BoardId = board.Id, Title = "Soal di Grup Tersembunyi", StatementMarkdown = "x", GroupId = secretGroup.Id };
            db.Problems.AddRange(open, hidden, notOpen, inHiddenGroup);
            await db.SaveChangesAsync();
            openId = open.Id; hiddenId = hidden.Id; notOpenId = notOpen.Id; hiddenGroupId = inHiddenGroup.Id;
        }
        async Task<HttpStatusCode> Statement(HttpClient c, int id) => (await c.GetAsync($"/api/problems/{id}/statement")).StatusCode;

        Assert.Equal(HttpStatusCode.OK, await Statement(kid, openId));
        Assert.Equal(HttpStatusCode.NotFound, await Statement(kid, hiddenId));
        Assert.Equal(HttpStatusCode.NotFound, await Statement(kid, notOpenId));
        Assert.Equal(HttpStatusCode.NotFound, await Statement(kid, hiddenGroupId));

        var teacher = app.CreateClient();   // the board's owner still sees everything, to prepare it
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        foreach (var id in new[] { openId, hiddenId, notOpenId, hiddenGroupId })
            Assert.Equal(HttpStatusCode.OK, await Statement(teacher, id));

        var outsider = app.CreateClient();      // not a member at all
        (await outsider.PostAsJsonAsync("/api/auth/register", new { email = "out@test.dev", password = "Passw0rd!x1", displayName = "Out", role = "Student" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, await Statement(outsider, openId));
    }
}
