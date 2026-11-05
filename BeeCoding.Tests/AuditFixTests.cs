using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Lti;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AuditFixTests
{
    [Fact]
    public async Task An_lti_launch_never_takes_over_an_existing_account_by_the_email_it_claims()
    {
        using var app = new TestApp();
        app.CreateClient();   // start the host (migrates + seeds)
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var victim = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
        var platform = new LtiPlatform { Name = "lms", Issuer = "https://lms.example", ClientId = "c", DeploymentIds = "d", AuthLoginUrl = "https://lms.example/a", AuthTokenUrl = "https://lms.example/t", JwksUrl = "https://lms.example/k" };
        db.LtiPlatforms.Add(platform);
        await db.SaveChangesAsync();

        var claims = new LtiLaunchClaims { MessageType = "LtiResourceLinkRequest", Issuer = platform.Issuer, Subject = "s1", DeploymentId = "d", Email = "Teacher@demo.test", Name = "Mallory" };
        var user = await scope.ServiceProvider.GetRequiredService<LtiProvisioningService>().FindOrCreateUserAsync(platform, claims);

        Assert.NotEqual(victim.Id, user.Id);
        Assert.EndsWith("@lti.invalid", user.Email);
    }

    [Fact]
    public async Task Simultaneous_wrong_email_codes_stop_at_five_attempts()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        (await http.PostAsJsonAsync("/api/auth/register", new { email = "mfa@test.dev", password = "Passw0rd!x1", displayName = "Mfa", role = "Student" })).EnsureSuccessStatusCode();
        int userId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = await db.Users.Where(u => u.Email == "mfa@test.dev").Select(u => u.Id).FirstAsync();
            db.EmailLoginCodes.Add(new EmailLoginCode { UserId = userId, CodeHash = "not-a-real-hash", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
            await db.SaveChangesAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var s = app.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            Assert.False(await s.ServiceProvider.GetRequiredService<MfaService>().VerifyEmailCodeAsync(user, $"{i:D6}"));
        }));

        using var check = app.Services.CreateScope();
        Assert.Equal(5, await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailLoginCodes.Where(c => c.UserId == userId).Select(c => c.Attempts).FirstAsync());
    }

    [Fact]
    public void Pending_lti_logins_are_capped()
    {
        var store = new LtiLoginStateStore();
        for (int i = 0; i < 5000; i++) Assert.NotNull(store.StartLogin(1, "n", "https://x"));
        Assert.Null(store.StartLogin(1, "n", "https://x"));
    }
}
