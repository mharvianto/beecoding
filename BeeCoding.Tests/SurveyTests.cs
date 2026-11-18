using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SurveyTests
{
    [Fact]
    public async Task A_user_keeps_one_editable_response_and_only_an_admin_reads_everyones_without_identity()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var kid = app.CreateClient();
            (await kid.PostAsJsonAsync("/api/auth/register", new { email = "kid@test.dev", password = "Passw0rd!x1", displayName = "Kid", role = "Student" })).EnsureSuccessStatusCode();

            (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "9", ["like"] = "  fast  ", ["empty"] = "  " })).EnsureSuccessStatusCode();
            (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["nps"] = "10" })).EnsureSuccessStatusCode();
            var mine = await kid.GetFromJsonAsync<Dictionary<string, string>>("/api/survey/mine");
            Assert.Equal(new Dictionary<string, string> { ["nps"] = "10" }, mine);
            using (var scope = app.Services.CreateScope())
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().SurveyResponses.CountAsync());

            Assert.Equal(HttpStatusCode.BadRequest, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["Bad Key!"] = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await kid.PutAsJsonAsync("/api/survey", new Dictionary<string, string> { ["like"] = new string('x', 2001) })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.GetAsync("/api/survey/results")).StatusCode);

            var boss = app.CreateClient();
            (await boss.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var res = await boss.GetAsync("/api/survey/results");
            res.EnsureSuccessStatusCode();
            var text = await res.Content.ReadAsStringAsync();
            Assert.Contains("\"nps\":\"10\"", text);
            Assert.DoesNotContain("kid@test.dev", text);
            Assert.DoesNotContain("userId", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}
