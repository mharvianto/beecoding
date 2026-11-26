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
}
