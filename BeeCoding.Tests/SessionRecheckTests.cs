using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SessionRecheckTests
{
    [Fact]
    public async Task A_deleted_user_loses_their_session_within_the_recheck_window_and_a_live_one_keeps_it()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", "1");
        try
        {
            using var app = new TestApp();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "gone@test.dev", password = "Passw0rd!x1", displayName = "Gone", role = "Student" })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await kid.GetAsync("/api/auth/me")).StatusCode);

            var admin = app.CreateClient();
            (await admin.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            int id;
            using (var scope = app.Services.CreateScope())
                id = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == "gone@test.dev").Select(u => u.Id).FirstAsync();
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin-ui/users/{id}")).StatusCode);

            HttpStatusCode last = HttpStatusCode.OK;
            for (int i = 0; i < 20 && last != HttpStatusCode.Unauthorized; i++)
            {
                await Task.Delay(300);
                last = (await kid.GetAsync("/api/auth/me")).StatusCode;
            }
            Assert.Equal(HttpStatusCode.Unauthorized, last);                                          // the cached look-up expired and saw the deletion
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);        // everyone else is untouched
        }
        finally
        {
            Environment.SetEnvironmentVariable("Admin__Emails", null);
            Environment.SetEnvironmentVariable("Security__SessionRecheckSeconds", null);
        }
    }
}
