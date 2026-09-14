using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services.Lti;

/// <summary>
/// Turns a validated launch into a BeeCoding session: find-or-create the User for the
/// platform's `sub`, find-or-create the Board for the resource_link placement, and make
/// sure the user is a member with the right role.
/// </summary>
public class LtiProvisioningService(AppDbContext db, PasswordService pw, BoardService boards)
{
    private readonly AppDbContext _db = db;
    private readonly PasswordService _pw = pw;
    private readonly BoardService _boards = boards;

    public async Task<User> FindOrCreateUserAsync(LtiPlatform platform, LtiLaunchClaims claims)
    {
        var link = await _db.LtiUserLinks.Include(l => l.User)
            .FirstOrDefaultAsync(l => l.LtiPlatformId == platform.Id && l.Subject == claims.Subject);
        if (link?.User is { DeletedAt: null } existing)
        {
            await BackfillIdentityAsync(existing, claims);
            return existing;
        }

        // Email is optional in LTI (a platform can withhold PII) — synthesize a stable,
        // non-routable one keyed by platform+subject so User.Email's unique index still holds.
        var email = string.IsNullOrWhiteSpace(claims.Email)
            ? $"lti-{platform.Id}-{claims.Subject}@lti.invalid"
            : claims.Email!.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            user = new User
            {
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(claims.Name) ? "LTI user" : claims.Name!,
                Role = LtiClaims.IsInstructor(claims.Roles) ? UserRole.Teacher : UserRole.Student,
            };
            // LTI-only account — password login stays possible in principle (e.g. if they
            // later set one via "forgot password"), but starts as an unguessable random hash.
            user.PasswordHash = _pw.Hash(user, $"{Guid.NewGuid():N}{Guid.NewGuid():N}");
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        if (link is null)
        {
            _db.LtiUserLinks.Add(new LtiUserLink { LtiPlatformId = platform.Id, Subject = claims.Subject, UserId = user.Id });
            await _db.SaveChangesAsync();
        }
        else if (link.UserId != user.Id)
        {
            link.UserId = user.Id;   // the linked account was purged; re-link to the (re)found/created one
            await _db.SaveChangesAsync();
        }

        // A platform tied to an organization auto-enrolls anyone who launches through it —
        // covers both a brand-new account and an existing one launching that org's LMS for
        // the first time. Never touches membership in any OTHER organization the user is in.
        if (platform.OrganizationId is int orgId)
        {
            var isMember = await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == orgId && m.UserId == user.Id);
            if (!isMember)
            {
                _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = orgId, UserId = user.Id, Role = OrgRole.Member });
                await _db.SaveChangesAsync();
            }
        }

        return user;
    }

    /// <summary>A first launch before the platform shared PII leaves the user with a
    /// synthesized "lti-...@lti.invalid" email and/or a "LTI user" display name (see
    /// above). If a later launch does carry real values — e.g. an admin turned on
    /// "share name/email with tool" in the platform's LTI privacy settings after the
    /// account already existed — adopt them now instead of leaving the placeholder
    /// forever. Never overwrites a name/email the user already has for real, and never
    /// claims an email another account is already using.</summary>
    private async Task BackfillIdentityAsync(User user, LtiLaunchClaims claims)
    {
        var changed = false;

        if (user.DisplayName == "LTI user" && !string.IsNullOrWhiteSpace(claims.Name))
        {
            user.DisplayName = claims.Name!;
            changed = true;
        }

        if (user.Email.EndsWith("@lti.invalid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(claims.Email))
        {
            var real = claims.Email!.Trim().ToLowerInvariant();
            var taken = await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email == real);
            if (!taken)
            {
                user.Email = real;
                changed = true;
            }
        }

        if (changed) await _db.SaveChangesAsync();
    }

    /// <summary>Null only for a first-ever launch of a placement by a non-instructor —
    /// there's nothing to auto-create a board from yet.</summary>
    public async Task<Board?> FindOrCreateBoardAsync(LtiPlatform platform, LtiLaunchClaims claims, User user, bool isInstructor)
    {
        var link = await _db.LtiResourceLinks.Include(l => l.Board)
            .FirstOrDefaultAsync(l => l.LtiPlatformId == platform.Id && l.DeploymentId == claims.DeploymentId
                && l.ContextId == claims.ContextId && l.ResourceLinkId == claims.ResourceLinkId);

        if (link?.Board is { DeletedAt: null })
        {
            if (claims.AgsLineItemUrl is not null && link.LineItemUrl != claims.AgsLineItemUrl)
            {
                link.LineItemUrl = claims.AgsLineItemUrl;
                link.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            return link.Board;
        }

        // First-ever launch of this placement. If it was created via the Deep Linking
        // picker, the board the teacher chose rides along in target_link_uri's query
        // string (?board=<slug>) — attach to that instead of minting a new board.
        var hintSlug = TryGetBoardHint(claims.TargetLinkUri);
        Board? board = hintSlug is not null
            ? await _db.Boards.FirstOrDefaultAsync(b => b.Slug == hintSlug)
            : null;

        if (board is null)
        {
            if (!isInstructor) return null;   // wait for an instructor to launch first
            board = new Board
            {
                Title = claims.ResourceLinkTitle ?? claims.ContextTitle ?? "LTI board",
                OwnerId = user.Id,
                OrganizationId = platform.OrganizationId,
                JoinCode = await _boards.GenerateJoinCodeAsync(),
                Slug = await _boards.GenerateSlugAsync(),
            };
            _db.Boards.Add(board);
            await _db.SaveChangesAsync();
        }

        if (link is null)
        {
            _db.LtiResourceLinks.Add(new LtiResourceLink
            {
                LtiPlatformId = platform.Id, DeploymentId = claims.DeploymentId,
                ContextId = claims.ContextId ?? "", ResourceLinkId = claims.ResourceLinkId ?? "",
                BoardId = board.Id, LineItemUrl = claims.AgsLineItemUrl,
            });
        }
        else
        {
            link.BoardId = board.Id;
            link.LineItemUrl = claims.AgsLineItemUrl;
            link.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return board;
    }

    public async Task EnsureMembershipAsync(Board board, User user, bool isInstructor)
    {
        var membership = await _db.BoardMemberships.FirstOrDefaultAsync(m => m.BoardId == board.Id && m.UserId == user.Id);
        var role = board.OwnerId == user.Id ? MembershipRole.Owner : isInstructor ? MembershipRole.Teacher : MembershipRole.Student;
        if (membership is null)
        {
            _db.BoardMemberships.Add(new BoardMembership { BoardId = board.Id, UserId = user.Id, Role = role });
            await _db.SaveChangesAsync();
        }
        else if (membership.Role != role && role != MembershipRole.Student)
        {
            // Only ever promotes (e.g. Student -> Teacher if their LTI role changed) —
            // never silently demotes someone who was granted staff access some other way.
            membership.Role = role;
            await _db.SaveChangesAsync();
        }
    }

    private static string? TryGetBoardHint(string? targetLinkUri)
    {
        if (targetLinkUri is null || !Uri.TryCreate(targetLinkUri, UriKind.Absolute, out var uri)) return null;
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return q.TryGetValue("board", out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
    }
}
