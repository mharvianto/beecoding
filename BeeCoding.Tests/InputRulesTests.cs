using System.Net;
using System.Net.Http.Json;
using BeeCoding.Services;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class InputRulesTests
{
    [Fact]
    public void Passwords_and_tags_have_an_upper_bound()
    {
        Assert.NotNull(PasswordPolicy.Validate(new string('a', 129) + "1", "x@y.dev"));
        Assert.Null(PasswordPolicy.Validate("Passw0rd!x1", "x@y.dev"));
        var tags = Mapping.NormalizeTags(new string('A', 500) + ",b,B");
        Assert.Equal(new string('a', 24) + ",b", tags);
        Assert.True(Mapping.NormalizeTags(string.Join(',', Enumerable.Range(0, 30).Select(i => new string('t', 40) + i))).Length <= 300);
    }

    [Fact]
    public async Task Names_are_checked_on_register_and_on_profile_edit()
    {
        using var app = new TestApp();
        async Task<HttpResponseMessage> Register(string email, string name) =>
            await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x1", displayName = name, role = "Student" });
        Assert.Equal(HttpStatusCode.BadRequest, (await Register("n1@test.dev", new string('x', 41))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Register("n2@test.dev", "A")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Register("n3@test.dev", " \u200B ")).StatusCode);

        var c = app.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/register", new { email = "n4@test.dev", password = "Passw0rd!x1", displayName = "  Budi   Santoso ", role = "Student" })).EnsureSuccessStatusCode();
        Task<HttpResponseMessage> Patch(string name) => c.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/auth/profile") { Content = JsonContent.Create(new { displayName = name }) });
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(new string('y', 41))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch("B")).StatusCode);
        var ok = await Patch("Budi S");
        ok.EnsureSuccessStatusCode();
        Assert.Contains("\"displayName\":\"Budi S\"", await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_rejects_odd_emails_and_board_titles_are_capped()
    {
        using var app = new TestApp();
        foreach (var bad in new[] { "a", "a@b", "x <a@b.dev>", "a b@c.dev", new string('a', 251) + "@b.dev" })
        {
            var r = await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = bad, password = "Passw0rd!x1", displayName = "Okay", role = "Student" });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        (await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "good.name+x@b.dev", password = "Passw0rd!x1", displayName = "Okay", role = "Student" })).EnsureSuccessStatusCode();

        var teacher = app.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync("/api/boards", new { title = new string('x', 161) })).StatusCode);
        (await teacher.PostAsJsonAsync("/api/boards", new { title = new string('x', 160) })).EnsureSuccessStatusCode();
    }
}
