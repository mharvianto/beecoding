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
    public async Task An_account_promoted_to_teacher_becomes_staff_of_a_board_it_joined_as_a_student_by_entering_the_code_again()
    {
        Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", "0");   // the promotion shows in the session at once
        try
        {
            using var app = new TestApp();
            var http = app.CreateClient();
            (await http.PostAsJsonAsync("/api/auth/register", new { email = "late@t.test", password = "password1!", displayName = "Late Teacher", role = "Student" })).EnsureSuccessStatusCode();

            var first = await http.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" });
            Assert.Equal("Student", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString());

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Users.Single(u => u.Email == "late@t.test").Role = UserRole.Teacher;   // what Admin → Users → Role does
                await db.SaveChangesAsync();
            }

            var again = await http.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" });
            Assert.Equal("Teacher", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString());
            var board = await http.GetFromJsonAsync<JsonElement>("/api/boards/demo-board");
            Assert.Equal("Teacher", board.GetProperty("role").GetString());
        }
        finally { Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", null); }
    }

    // Same class as the test above on purpose: TestApp sets a process-wide connection-string variable, so two classes starting at once can share a database.
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
            Assert.Equal(System.Net.HttpStatusCode.NoContent, (await owner.PostAsync($"/api/boards/demo-board/members/{Id("late2@t.test")}/make-teacher", null)).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/boards/demo-board/members/{Id("plain@t.test")}/make-teacher", null)).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await plain.PostAsync($"/api/boards/demo-board/members/{Id("late2@t.test")}/make-teacher", null)).StatusCode);

            Assert.Equal("Teacher", (await late.GetFromJsonAsync<JsonElement>("/api/boards/demo-board")).GetProperty("role").GetString());
            Assert.Equal("Student", (await plain.GetFromJsonAsync<JsonElement>("/api/boards/demo-board")).GetProperty("role").GetString());
        }
        finally { Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", null); }
    }
}
