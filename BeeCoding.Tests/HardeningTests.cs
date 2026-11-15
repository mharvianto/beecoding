using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Lti;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class HardeningTests
{
    [Fact]
    public async Task Join_code_guesses_are_counted_and_a_correct_code_still_works_for_someone_who_has_not_guessed()
    {
        using var app = new TestApp();
        var guesser = app.CreateClient();
        (await guesser.PostAsJsonAsync("/api/auth/register", new { email = "guess@test.dev", password = "Passw0rd!x1", displayName = "Guess", role = "Student" })).EnsureSuccessStatusCode();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await guesser.PostAsJsonAsync("/api/boards/join", new { code = $"ZZZZ{i:00}" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guesser.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).StatusCode);   // blocked even for the right code

        var honest = app.CreateClient();
        (await honest.PostAsJsonAsync("/api/auth/register", new { email = "honest@test.dev", password = "Passw0rd!x1", displayName = "Honest", role = "Student" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await honest.PostAsJsonAsync("/api/boards/join", new { code = "NOPE99" })).StatusCode);   // a single typo is fine
        Assert.Equal(HttpStatusCode.OK, (await honest.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).StatusCode);
    }

    [Fact]
    public async Task A_live_draft_for_a_problem_of_another_board_is_not_stored()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" });
        login.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        int boardId, mine, foreign;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
            var other = new Board { Title = "Other", OwnerId = owner.Id, JoinCode = "OTHR01" };
            db.Boards.Add(other); await db.SaveChangesAsync();
            var a = new Problem { BoardId = board.Id, Title = "Mine", StatementMarkdown = "x" };
            var b = new Problem { BoardId = other.Id, Title = "Foreign", StatementMarkdown = "x" };
            db.Problems.AddRange(a, b); await db.SaveChangesAsync();
            boardId = board.Id; mine = a.Id; foreign = b.Id;
        }
        var wsc = app.Server.CreateWebSocketClient();
        wsc.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
        using var ws = await wsc.ConnectAsync(new Uri("ws://localhost/hubs/board"), CancellationToken.None);
        const char Sep = '\u001e';
        async Task Send(string json) => await ws.SendAsync(Encoding.UTF8.GetBytes(json + Sep), WebSocketMessageType.Text, true, CancellationToken.None);
        async Task Until(string needle)
        {
            var seen = new StringBuilder(); var buf = new byte[16 * 1024]; using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!seen.ToString().Contains(needle)) { var r = await ws.ReceiveAsync(buf, cts.Token); seen.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); }
        }
        await Send("{\"protocol\":\"json\",\"version\":1}"); await Until("{}");
        await Send("{\"type\":1,\"invocationId\":\"1\",\"target\":\"PushDraft\",\"arguments\":[" + boardId + "," + foreign + ",\"int main(){}\"]}"); await Until("\"invocationId\":\"1\"");
        await Task.Delay(400);   // the hub allows one draft per ~250 ms per connection
        await Send("{\"type\":1,\"invocationId\":\"2\",\"target\":\"PushDraft\",\"arguments\":[" + boardId + "," + mine + ",\"int main(){}\"]}"); await Until("\"invocationId\":\"2\"");

        var drafts = await app.Services.GetRequiredService<IDraftStore>().ForBoardAsync(boardId);
        Assert.Contains(drafts, d => d.ProblemId == mine);
        Assert.DoesNotContain(drafts, d => d.ProblemId == foreign);
    }

    [Fact]
    public async Task An_lti_launch_hint_cannot_bind_a_board_the_platform_has_no_claim_to()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<LtiProvisioningService>();
        var org = new Organization { Name = "A", Slug = "a-lti" };
        db.Organizations.Add(org); await db.SaveChangesAsync();
        var platform = new LtiPlatform { Name = "P", Issuer = "https://lms.example", ClientId = "c", DeploymentIds = "d1", OrganizationId = org.Id, Enabled = true };
        db.LtiPlatforms.Add(platform);
        var lecturer = new User { Email = "lti-lecturer@lti.invalid", DisplayName = "Lecturer", Role = UserRole.Teacher };
        db.Users.Add(lecturer); await db.SaveChangesAsync();
        var foreign = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");            // owned by the demo teacher: not this platform's
        var own = new Board { Title = "Ours", OwnerId = lecturer.Id, OrganizationId = org.Id, JoinCode = "OURS01", Slug = "oursslug0001" };
        db.Boards.Add(own); await db.SaveChangesAsync();

        LtiLaunchClaims Claims(string slug, string link) => new()
        {
            MessageType = "LtiResourceLinkRequest", Issuer = platform.Issuer, Subject = "s1", DeploymentId = "d1", ContextId = "ctx", ResourceLinkId = link,
            Roles = ["http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor"], TargetLinkUri = $"https://app.example/lti/launch?board={slug}",
        };

        var (bound, _) = await svc.ResolveResourceLinkAsync(platform, Claims(foreign.Slug, "rl-foreign"), lecturer, isInstructor: true);
        Assert.NotNull(bound);
        Assert.NotEqual(foreign.Id, bound!.Id);                                            // a new board was made instead of binding the foreign one
        Assert.False(await db.BoardMemberships.AnyAsync(m => m.BoardId == foreign.Id && m.UserId == lecturer.Id));

        var (ours, _) = await svc.ResolveResourceLinkAsync(platform, Claims(own.Slug, "rl-own"), lecturer, isInstructor: true);
        Assert.Equal(own.Id, ours!.Id);                                                    // a board of the platform's own organization still binds
    }

    [Fact]
    public async Task Turning_on_an_authenticator_and_getting_a_google_link_ticket_both_need_the_password()
    {
        using var app = new TestApp();
        var c = app.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        (await c.PostAsync("/api/auth/mfa/totp/setup", null)).EnsureSuccessStatusCode();

        var noPw = await c.PostAsJsonAsync("/api/auth/mfa/totp/enable", new { code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, noPw.StatusCode);
        Assert.Contains("Password is wrong", await noPw.Content.ReadAsStringAsync());              // refused before the code is even looked at
        var wrongPw = await c.PostAsJsonAsync("/api/auth/mfa/totp/enable", new { code = "000000", password = "nope" });
        Assert.Contains("Password is wrong", await wrongPw.Content.ReadAsStringAsync());
        var rightPwWrongCode = await c.PostAsJsonAsync("/api/auth/mfa/totp/enable", new { code = "000000", password = "password" });
        Assert.DoesNotContain("Password is wrong", await rightPwWrongCode.Content.ReadAsStringAsync());   // now it is the code that fails

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/google/link-ticket", new { password = "nope" })).StatusCode);
        var ok = await c.PostAsJsonAsync("/api/auth/google/link-ticket", new { password = "password" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ok.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("ticket").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsJsonAsync("/api/auth/google/link-ticket", new { password = "password" })).StatusCode);
    }
}
