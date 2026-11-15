using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class SecurityFixTests
{
    [Fact]
    public async Task A_student_cannot_read_teacher_code_or_anything_on_a_hidden_problem_through_the_submission_endpoints()
    {
        using var app = new TestApp();
        var kid = app.CreateClient();
        (await kid.PostAsJsonAsync("/api/auth/register", new { email = "reader@test.dev", password = "Passw0rd!x1", displayName = "Reader", role = "Student" })).EnsureSuccessStatusCode();
        (await kid.PostAsJsonAsync("/api/boards/join", new { code = "DEMO01" })).EnsureSuccessStatusCode();
        int hiddenId, openId, teacherOnHidden, teacherOnOpen, peerOnOpen;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var board = await db.Boards.FirstAsync(b => b.JoinCode == "DEMO01");
            var teacher = await db.Users.FirstAsync(u => u.Email == "teacher@demo.test");
            var reader = await db.Users.FirstAsync(u => u.Email == "reader@test.dev");
            var peer = new User { Email = "peer@test.dev", DisplayName = "Peer", Role = UserRole.Student };
            db.Users.Add(peer); await db.SaveChangesAsync();
            db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = peer.Id, Role = MembershipRole.Student });
            var hidden = new Problem { BoardId = board.Id, Title = "Hidden", StatementMarkdown = "x", Hidden = true };
            var open = new Problem { BoardId = board.Id, Title = "Open", StatementMarkdown = "x" };
            db.Problems.AddRange(hidden, open); await db.SaveChangesAsync();
            Submission S(Problem p, User u, string code) { var s = new Submission { ProblemId = p.Id, UserId = u.Id, Code = code, Status = SubmissionStatus.Done }; db.Submissions.Add(s); return s; }
            var a = S(hidden, teacher, "REFERENCE-HIDDEN"); var b = S(open, teacher, "REFERENCE-OPEN"); var c = S(open, peer, "PEER-CODE");
            await db.SaveChangesAsync();
            hiddenId = hidden.Id; openId = open.Id; teacherOnHidden = a.Id; teacherOnOpen = b.Id; peerOnOpen = c.Id;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync($"/api/problems/{hiddenId}/submissions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync($"/api/submissions/{teacherOnHidden}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kid.GetAsync($"/api/submissions/{teacherOnOpen}")).StatusCode);   // visible problem, but staff code stays staff-only
        var listed = await kid.GetStringAsync($"/api/problems/{openId}/submissions");
        Assert.DoesNotContain("REFERENCE-OPEN", listed);
        Assert.Contains("PEER-CODE", listed);                                                                            // peers' solutions keep working as before
        Assert.Equal(HttpStatusCode.OK, (await kid.GetAsync($"/api/submissions/{peerOnOpen}")).StatusCode);

        var staff = app.CreateClient();
        (await staff.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
        Assert.Contains("REFERENCE-HIDDEN", await staff.GetStringAsync($"/api/problems/{hiddenId}/submissions"));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/submissions/{teacherOnHidden}")).StatusCode);
    }

    [Fact]
    public async Task A_platform_admin_cannot_delete_their_own_account_or_be_purged_so_the_address_cannot_be_claimed()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var t = app.CreateClient();
            (await t.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();

            var del = await t.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/account") { Content = JsonContent.Create(new { password = "password", deleteOwnedBoards = true }) });
            Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);

            int id;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var u = new User { Email = "teacher@demo.test".Replace("teacher", "gone"), DisplayName = "Gone", Role = UserRole.Student };   // an ordinary account is still purgeable
                db.Users.Add(u); await db.SaveChangesAsync(); id = u.Id;
            }
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (await db.Users.FirstAsync(u => u.Email == "teacher@demo.test")).DeletedAt = DateTime.UtcNow;
                (await db.Users.FirstAsync(u => u.Id == id)).DeletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            var adminId = await app.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == "teacher@demo.test").Select(u => u.Id).FirstAsync();
            // the soft-deleted admin session is still the caller's cookie; the admin policy follows the email, so the purge endpoint is reachable
            Assert.Equal(HttpStatusCode.Conflict, (await t.DeleteAsync($"/api/admin-ui/trash/users/{adminId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await t.DeleteAsync($"/api/admin-ui/trash/users/{id}")).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }

    [Fact]
    public async Task The_demo_teacher_is_seeded_in_development_but_not_in_production()
    {
        Environment.SetEnvironmentVariable("Judge__RequireSandbox", "false");   // this container cannot run bwrap; Production would refuse to start
        try
        {
            using var app = new TestApp();
            using var prod = app.WithWebHostBuilder(b => b.UseEnvironment("Production"));
            _ = prod.CreateClient();
            using var scope = prod.Services.CreateScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
        }
        finally { Environment.SetEnvironmentVariable("Judge__RequireSandbox", null); }
        using var dev = new TestApp();
        _ = dev.CreateClient();
        using var scope2 = dev.Services.CreateScope();
        Assert.True(await scope2.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Email == "teacher@demo.test"));
    }
}
