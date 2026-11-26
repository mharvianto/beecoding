using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AdminMfaHeldTests
{
    [Fact]
    public async Task An_admin_without_two_step_works_as_an_ordinary_user_until_it_is_on()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        (await http.PostAsJsonAsync("/api/auth/register", new { email = "new-admin@t.test", password = "password1!", displayName = "New Admin", role = "Student" })).EnsureSuccessStatusCode();
        async Task<JsonElement> Me() => await http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        async Task Change(Action<Models.User> edit)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            edit(db.Users.Single(u => u.Email == "new-admin@t.test"));
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<PlatformRuntimeConfig>().SetMfaPolicy(admin: true, orgAdmin: false, teacher: false);
        await Change(u => u.IsAdmin = true);
        app.Services.GetRequiredService<AdminAccess>().SetDbAdmin("new-admin@t.test", true);   // what "Make admin" does

        // held: the ordinary app works, nothing an admin may do does (policy attribute, hand-written check, org-wide access)
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/boards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/admin-ui/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/boards/demo-board")).StatusCode);
        var me = await Me();
        Assert.False(me.GetProperty("isAdmin").GetBoolean());
        Assert.True(me.GetProperty("adminHeld").GetBoolean());
        Assert.False(me.GetProperty("mfaRequired").GetBoolean());

        await Change(u => u.EmailMfaEnabledAt = DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/admin-ui/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/boards/demo-board")).StatusCode);
        Assert.True((await Me()).GetProperty("isAdmin").GetBoolean());

        // turned off again: held again at once
        await Change(u => u.EmailMfaEnabledAt = null);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/admin-ui/users")).StatusCode);

        // the teacher rule still locks the whole app
        app.Services.GetRequiredService<PlatformRuntimeConfig>().SetMfaPolicy(admin: true, orgAdmin: false, teacher: true);
        await Change(u => u.Role = Models.UserRole.Teacher);
        app.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove(CookieSignIn.CacheKey((await Me()).GetProperty("id").GetInt32()));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/boards")).StatusCode);
        Assert.True((await Me()).GetProperty("mfaRequired").GetBoolean());
    }
}
