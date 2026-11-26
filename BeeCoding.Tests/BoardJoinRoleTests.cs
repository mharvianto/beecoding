using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

public class BoardJoinRoleTests
{
    [Fact]
    public async Task The_owner_can_lift_a_teachers_account_to_teacher_of_the_board_but_not_a_students()
    {
        Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", "0");
        try
        {
            using var app = new TestApp();
            async Task<HttpClient> Student(string email, bool teacherAccount)
            {
                var c = app.CreateClient();
                (await c.PostAsJsonAsync("/api/auth/register", new { email, password = "password1!", displayName = email, role = "Student" })).EnsureSuccessStatusCode();
                (await c.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
                if (teacherAccount)
                {
                    using var scope = app.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Users.Single(u => u.Email == email).Role = UserRole.Teacher;
                    await db.SaveChangesAsync();
                }
                return c;
            }
            var late = await Student("late2@t.test", teacherAccount: true);
            var plain = await Student("plain@t.test", teacherAccount: false);
            int Id(string email) { using var s = app.Services.CreateScope(); return s.ServiceProvider.GetRequiredService<AppDbContext>().Users.Single(u => u.Email == email).Id; }

            var owner = app.CreateClient();
            (await owner.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/boards/demo-board/members/{Id("late2@t.test")}/make-teacher", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/boards/demo-board/members/{Id("plain@t.test")}/make-teacher", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync($"/api/boards/demo-board/members/{Id("late2@t.test")}/make-teacher", null)).StatusCode);

            Assert.Equal("Teacher", (await late.GetFromJsonAsync<JsonElement>("/api/boards/demo-board")).GetProperty("role").GetString());
            Assert.Equal("Student", (await plain.GetFromJsonAsync<JsonElement>("/api/boards/demo-board")).GetProperty("role").GetString());
        }
        finally { Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", null); }
    }

    [Fact]
    public async Task A_teacher_can_leave_and_only_the_owner_can_demote_or_remove_one_and_nobody_the_owner()
    {
        using var app = new TestApp();
        async Task<(HttpClient Http, int Id)> Member(string email)
        {
            var c = app.CreateClient();
            (await c.PostAsJsonAsync("/api/auth/register", new { email, password = "password1!", displayName = email, role = "Student" })).EnsureSuccessStatusCode();
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = db.Users.Single(u => u.Email == email).Id;
            db.BoardMemberships.Add(new BoardMembership { BoardId = db.Boards.Single(b => b.JoinCode == "DEMO01").Id, UserId = id, Role = MembershipRole.Teacher });
            await db.SaveChangesAsync();
            return (c, id);
        }
        var (one, oneId) = await Member("one@t.test");
        var (two, twoId) = await Member("two@t.test");
        var owner = app.CreateClient();
        (await owner.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        int ownerId;
        using (var scope = app.Services.CreateScope()) ownerId = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Single(u => u.Email == "teacher@demo.test").Id;
        string Url(int id) => $"/api/boards/demo-board/members/{id}";

        // a co-teacher cannot touch another teacher or the owner
        Assert.Equal(HttpStatusCode.Forbidden, (await one.DeleteAsync(Url(twoId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await one.PostAsync(Url(twoId) + "/make-student", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await one.DeleteAsync(Url(ownerId))).StatusCode);
        // the owner can neither leave nor be demoted
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync(Url(ownerId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(Url(ownerId) + "/make-student", null)).StatusCode);

        // demoted: a student, who stays one after entering the code again and cannot "leave" like a teacher
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync(Url(twoId) + "/make-student", null)).StatusCode);
        var again = await two.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" });
        Assert.Equal("Student", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await two.DeleteAsync(Url(twoId))).StatusCode);

        // a teacher leaves; the owner removes
        Assert.Equal(HttpStatusCode.NoContent, (await one.DeleteAsync(Url(oneId))).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await one.GetAsync("/api/boards/demo-board")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Url(twoId))).StatusCode);
    }
}
