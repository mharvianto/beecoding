using System.Net;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class AccountErasureTests
{
    private static async Task<(int pub, int draft, int sub)> SeedAsync(TestApp app, int authorId, int otherId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pub = new BankProblem { OwnerId = authorId, Title = "Public", StatementMarkdown = "s", IsPublic = true };
        var draft = new BankProblem { OwnerId = authorId, Title = "Draft", StatementMarkdown = "s" };
        db.BankProblems.AddRange(pub, draft);
        var s = new BankSubmission { BankProblem = pub, UserId = otherId, Code = "x", Status = SubmissionStatus.Done, Verdict = Verdict.Accepted, Score = 1 };
        db.BankSubmissions.Add(s);
        await db.SaveChangesAsync();
        return (pub.Id, draft.Id, s.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deleting_an_account_keeps_the_public_problems_as_Deleted_user_unless_told_not_to(bool keep)
    {
        using var app = new TestApp();
        var author = app.CreateClient();
        (await author.PostAsJsonAsync("/api/auth/register", new { email = "au@test.dev", password = "Passw0rd!x1", displayName = "Ayu", role = "Student" })).EnsureSuccessStatusCode();
        var other = app.CreateClient();
        (await other.PostAsJsonAsync("/api/auth/register", new { email = "ot@test.dev", password = "Passw0rd!x1", displayName = "Oto", role = "Student" })).EnsureSuccessStatusCode();
        int authorId, otherId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            authorId = (await db.Users.FirstAsync(u => u.Email == "au@test.dev")).Id;
            otherId = (await db.Users.FirstAsync(u => u.Email == "ot@test.dev")).Id;
        }
        var (pub, draft, sub) = await SeedAsync(app, authorId, otherId);

        var res = await author.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/account") { Content = JsonContent.Create(new { password = "Passw0rd!x1", keepPublicProblems = keep }) });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id == authorId));
            Assert.False(await db.BankProblems.AnyAsync(b => b.Id == draft));          // a private draft never survives
            var p = await db.BankProblems.Include(b => b.Owner).FirstOrDefaultAsync(b => b.Id == pub);
            if (!keep)
            {
                Assert.Null(p);
                Assert.False(await db.BankSubmissions.AnyAsync(s => s.Id == sub));
                return;
            }
            Assert.NotNull(p);
            Assert.True(p!.Owner!.IsSystem);
            Assert.Equal("Deleted user", p.Owner.DisplayName);
            Assert.NotNull(p.Owner.DeletedAt);                                          // nobody can sign in as it
            Assert.True(await db.BankSubmissions.AnyAsync(s => s.Id == sub));          // the other student keeps their history
        }

        var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/login", new { email = AccountErasure.SystemEmail, password = "" })).StatusCode);
    }

    [Fact]
    public async Task The_retention_sweep_purges_old_trashed_accounts_keeps_their_public_problems_and_leaves_the_placeholder_alone()
    {
        using var app = new TestApp();
        app.CreateClient();
        var runtime = app.Services.GetRequiredService<PlatformRuntimeConfig>();
        int oldId, youngId, pub;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            User U(string e, DateTime deleted) => new() { Email = e, DisplayName = e, Role = UserRole.Student, PasswordHash = "x", DeletedAt = deleted };
            var o = U("old@test.dev", DateTime.UtcNow.AddDays(-100)); var y = U("young@test.dev", DateTime.UtcNow.AddDays(-5));
            db.Users.AddRange(o, y); await db.SaveChangesAsync();
            var p = new BankProblem { OwnerId = o.Id, Title = "Mine", StatementMarkdown = "s", IsPublic = true };
            db.BankProblems.Add(p); await db.SaveChangesAsync();
            (oldId, youngId, pub) = (o.Id, y.Id, p.Id);
        }
        var janitor = new DeletedUserJanitor(app.Services.GetRequiredService<IServiceScopeFactory>(), runtime, NullLogger<DeletedUserJanitor>.Instance);

        runtime.SetDeletedUserRetention(0);                // off
        await janitor.SweepAsync(CancellationToken.None);
        using (var scope = app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Id == oldId));

        runtime.SetDeletedUserRetention(90);
        await janitor.SweepAsync(CancellationToken.None);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id == oldId));                       // purged
            Assert.True(await db.Users.AnyAsync(u => u.Id == youngId));                      // still inside the window
            Assert.True((await db.BankProblems.Include(b => b.Owner).FirstAsync(b => b.Id == pub)).Owner!.IsSystem);
        }
        using (var scope = app.Services.CreateScope())   // however old its soft-delete stamp is, the placeholder is never swept
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.IsSystem).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletedAt, DateTime.UtcNow.AddDays(-500)));
        }
        await janitor.SweepAsync(CancellationToken.None);
        using (var scope = app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.IsSystem));
    }
}
