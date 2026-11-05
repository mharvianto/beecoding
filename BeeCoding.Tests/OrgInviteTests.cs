using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class OrgInviteTests
{
    private static async Task<HttpClient> SignInAsync(TestApp app, string email, string displayName)
    {
        var c = app.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!x1", displayName, role = "Student" })).EnsureSuccessStatusCode();
        return c;
    }

    private static async Task<(HttpClient Admin, int OrgId)> OrgWithAdminAsync(TestApp app)
    {
        var admin = await SignInAsync(app, "orgadmin@test.dev", "Org Admin");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = new Organization { Name = "Dept A", Slug = "dept-a" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var adminId = await db.Users.Where(u => u.Email == "orgadmin@test.dev").Select(u => u.Id).FirstAsync();
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = org.Id, UserId = adminId, Role = OrgRole.Admin });
        await db.SaveChangesAsync();
        return (admin, org.Id);
    }

    [Fact]
    public async Task An_account_outside_the_orgs_domains_is_invited_and_joins_only_by_accepting()
    {
        using var app = new TestApp();
        var (admin, orgId) = await OrgWithAdminAsync(app);
        var guest = await SignInAsync(app, "guest@other.dev", "Guest");

        var add = await admin.PostAsJsonAsync($"/api/org-admin/{orgId}/members", new { email = "guest@other.dev", orgRole = "Member" });
        Assert.Equal(HttpStatusCode.Accepted, add.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == orgId && m.User!.Email == "guest@other.dev"));
        }

        var invites = await guest.GetFromJsonAsync<JsonElement>("/api/org-invites");
        Assert.Equal(1, invites.GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync($"/api/org-invites/{invites[0].GetProperty("id").GetInt32()}/accept", null)).StatusCode);

        var orgs = await guest.GetFromJsonAsync<JsonElement>("/api/org-invites/memberships");
        Assert.Equal(1, orgs.GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await guest.DeleteAsync($"/api/org-invites/memberships/{orgId}")).StatusCode);   // and can leave again
    }

    [Fact]
    public async Task An_account_on_the_orgs_own_domain_is_added_at_once()
    {
        using var app = new TestApp();
        var (admin, orgId) = await OrgWithAdminAsync(app);
        await SignInAsync(app, "kid@campus.test", "Kid");

        // the org's own admin cannot set domains (they skip the member's consent) — only the platform admin can
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.PutAsJsonAsync($"/api/org-admin/{orgId}/settings", new { emailDomains = "campus.test" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Organizations.FindAsync(orgId))!.EmailDomains = "campus.test";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/org-admin/{orgId}/members", new { email = "kid@campus.test", orgRole = "Member" })).StatusCode);
    }

    [Fact]
    public async Task A_members_private_bank_problem_is_not_offered_to_the_org_admin()
    {
        using var app = new TestApp();
        var (admin, orgId) = await OrgWithAdminAsync(app);
        await SignInAsync(app, "member@campus.test", "Member");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var memberId = await db.Users.Where(u => u.Email == "member@campus.test").Select(u => u.Id).FirstAsync();
            db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = orgId, UserId = memberId });
            db.BankProblems.Add(new BankProblem { OwnerId = memberId, Title = "Secret exam", Slug = "secret-exam", StatementMarkdown = "x", IsPublic = false });
            await db.SaveChangesAsync();
        }

        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/org-admin/{orgId}/bank");
        Assert.DoesNotContain(list.EnumerateArray(), p => p.GetProperty("title").GetString() == "Secret exam");
    }

    [Theory]
    [InlineData("@Univ.ac.id; student.univ.ac.id", "univ.ac.id, student.univ.ac.id")]
    [InlineData("", "")]
    public void Domains_are_normalised(string typed, string stored) => Assert.Equal(stored, OrgDomains.Normalize(typed).Value);

    [Fact]
    public void Domains_match_exactly_or_as_a_subdomain_only()
    {
        Assert.True(OrgDomains.Matches("univ.ac.id", "a@univ.ac.id"));
        Assert.True(OrgDomains.Matches("univ.ac.id", "a@mail.univ.ac.id"));
        Assert.False(OrgDomains.Matches("univ.ac.id", "a@notuniv.ac.id"));
        Assert.False(OrgDomains.Matches("", "a@univ.ac.id"));
        Assert.NotNull(OrgDomains.Normalize("not a domain!").Error);
    }
}
