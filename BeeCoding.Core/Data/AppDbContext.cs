using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMembership> BoardMemberships => Set<BoardMembership>();
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostReaction> PostReactions => Set<PostReaction>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<BankProblem> BankProblems => Set<BankProblem>();
    public DbSet<BankTestCase> BankTestCases => Set<BankTestCase>();
    public DbSet<BankSubmission> BankSubmissions => Set<BankSubmission>();
    public DbSet<SolveRecord> SolveRecords => Set<SolveRecord>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<AiHintProgress> AiHintProgresses => Set<AiHintProgress>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<PlatformRuntimeSettings> PlatformRuntimeSettings => Set<PlatformRuntimeSettings>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<AiProviderConfig> AiProviderConfigs => Set<AiProviderConfig>();
    public DbSet<AiUserSetting> AiUserSettings => Set<AiUserSetting>();
    public DbSet<LtiPlatform> LtiPlatforms => Set<LtiPlatform>();
    public DbSet<LtiUserLink> LtiUserLinks => Set<LtiUserLink>();
    public DbSet<LtiResourceLink> LtiResourceLinks => Set<LtiResourceLink>();
    public DbSet<LtiToolKey> LtiToolKeys => Set<LtiToolKey>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        b.Entity<Board>().HasIndex(x => x.JoinCode).IsUnique();
        b.Entity<Board>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Board>().HasQueryFilter(x => x.DeletedAt == null);
        b.Entity<Board>()
            .HasOne(x => x.Owner).WithMany()
            .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Board>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<BoardMembership>().HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();
        b.Entity<BoardMembership>()
            .HasOne(x => x.Board).WithMany(x => x.Members)
            .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BoardMembership>()
            .HasOne(x => x.User).WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Problem>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Problem>().HasQueryFilter(x => x.DeletedAt == null);
        b.Entity<Problem>()
            .HasOne(x => x.Board).WithMany(x => x.Problems)
            .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<TestCase>()
            .HasOne(x => x.Problem).WithMany(x => x.TestCases)
            .HasForeignKey(x => x.ProblemId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Submission>()
            .HasOne(x => x.Problem).WithMany()
            .HasForeignKey(x => x.ProblemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Submission>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Submission>().HasIndex(x => new { x.ProblemId, x.UserId });

        b.Entity<Post>().HasIndex(x => new { x.ProblemId, x.UserId }).IsUnique();
        b.Entity<Post>().HasIndex(x => x.BoardId);
        b.Entity<Post>()
            .HasOne(x => x.Board).WithMany()
            .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Post>()
            .HasOne(x => x.Problem).WithMany()
            .HasForeignKey(x => x.ProblemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Post>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<PostReaction>().HasIndex(x => new { x.PostId, x.UserId, x.Emoji }).IsUnique();
        b.Entity<PostReaction>()
            .HasOne(x => x.Post).WithMany(p => p.Reactions)
            .HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PostReaction>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<BankProblem>().HasIndex(x => x.OwnerId);
        b.Entity<BankProblem>().HasIndex(x => x.IsPublic);
        b.Entity<BankProblem>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<BankProblem>().HasQueryFilter(x => x.DeletedAt == null);
        b.Entity<BankProblem>()
            .HasOne(x => x.Owner).WithMany()
            .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BankTestCase>()
            .HasOne(x => x.BankProblem).WithMany(p => p.TestCases)
            .HasForeignKey(x => x.BankProblemId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<BankSubmission>().HasIndex(x => new { x.BankProblemId, x.UserId });
        b.Entity<BankSubmission>()
            .HasOne(x => x.BankProblem).WithMany()
            .HasForeignKey(x => x.BankProblemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BankSubmission>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<SolveRecord>().HasIndex(x => new { x.UserId, x.ProblemKey }).IsUnique();
        b.Entity<SolveRecord>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<PostComment>().HasIndex(x => x.PostId);
        b.Entity<PostComment>()
            .HasOne(x => x.Post).WithMany(p => p.Comments)
            .HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PostComment>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<AiUsage>().HasIndex(x => new { x.UserId, x.Day }).IsUnique();
        b.Entity<AiUsage>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<AiHintProgress>().HasIndex(x => new { x.UserId, x.ProblemKey }).IsUnique();
        b.Entity<AiHintProgress>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // No FK to Users: an audit row must survive the actor being purged.
        b.Entity<AuditLogEntry>().HasIndex(x => x.CreatedAt);

        b.Entity<AiUserSetting>().HasKey(x => x.UserId);
        b.Entity<AiUserSetting>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<LtiPlatform>().HasIndex(x => new { x.Issuer, x.ClientId }).IsUnique();

        b.Entity<LtiUserLink>().HasIndex(x => new { x.LtiPlatformId, x.Subject }).IsUnique();
        b.Entity<LtiUserLink>()
            .HasOne(x => x.LtiPlatform).WithMany()
            .HasForeignKey(x => x.LtiPlatformId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LtiUserLink>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<LtiResourceLink>().HasIndex(x => new { x.LtiPlatformId, x.DeploymentId, x.ContextId, x.ResourceLinkId }).IsUnique();
        b.Entity<LtiResourceLink>()
            .HasOne(x => x.LtiPlatform).WithMany()
            .HasForeignKey(x => x.LtiPlatformId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LtiResourceLink>()
            .HasOne(x => x.Board).WithMany()
            .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<LtiResourceLink>()
            .HasOne(x => x.BankProblem).WithMany()
            .HasForeignKey(x => x.BankProblemId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<LtiPlatform>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Organization>().HasIndex(x => x.Slug).IsUnique();

        b.Entity<OrganizationMembership>().HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        b.Entity<OrganizationMembership>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrganizationMembership>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // Id=1 is always the platform-wide default (OrganizationId == null); every other
        // row is one organization's own override.
        b.Entity<AiSettings>().HasIndex(x => x.OrganizationId).IsUnique().HasFilter("OrganizationId IS NOT NULL");
        b.Entity<AiSettings>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<AiProviderConfig>().HasIndex(x => x.OrganizationId).IsUnique().HasFilter("OrganizationId IS NOT NULL");
        b.Entity<AiProviderConfig>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignSlugs();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignSlugs();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>Give every tracked <see cref="IHasSlug"/> that still lacks one a unique
    /// random slug (covers fresh inserts and any legacy row loaded for backfill).</summary>
    private void AssignSlugs()
    {
        // IgnoreQueryFilters(): the unique index on Slug applies to every row in the table,
        // soft-deleted or not, so uniqueness must be checked against all of them too.
        var pending = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in ChangeTracker.Entries<Problem>())
            if (e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged
                && string.IsNullOrEmpty(e.Entity.Slug))
                e.Entity.Slug = FreshSlug(pending, s => Problems.IgnoreQueryFilters().Any(p => p.Slug == s));
        foreach (var e in ChangeTracker.Entries<BankProblem>())
            if (e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged
                && string.IsNullOrEmpty(e.Entity.Slug))
                e.Entity.Slug = FreshSlug(pending, s => BankProblems.IgnoreQueryFilters().Any(p => p.Slug == s));
    }

    private static string FreshSlug(HashSet<string> pending, Func<string, bool> takenInDb)
    {
        for (var i = 0; i < 25; i++)
        {
            var s = Slug.New();
            if (pending.Add(s) && !takenInDb(s)) return s;
        }
        return Slug.New(12);   // astronomically unlikely; longer slug as a last resort
    }
}
