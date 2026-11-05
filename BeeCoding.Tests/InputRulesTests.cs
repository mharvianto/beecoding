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
