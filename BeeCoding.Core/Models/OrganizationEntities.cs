using System.ComponentModel.DataAnnotations;

namespace BeeCoding.Models;

public enum OrgRole { Member, Admin }

/// <summary>
/// A tenant — typically a separate university/institution sharing this one deployment.
/// Board.OrganizationId and AiSettings.OrganizationId scope class ownership and AI cost
/// control per organization; OrganizationMembership is many-to-many (a user can belong to
/// several, or none — "none" is a normal, fully-supported state, not an edge case).
/// </summary>
public class Organization
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = "";

    [MaxLength(64)]
    public string Slug { get; set; } = "";

    /// <summary>Comma-separated email domains ("binus.ac.id, student.binus.ac.id"). An org admin adds an account with one of
    /// these domains straight away; anyone else gets an invitation they must accept. Empty = every addition is an invitation
    /// (the safe default: organizations can be departments of one university, so a shared domain says little).</summary>
    [MaxLength(400)]
    public string EmailDomains { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// An org admin's offer to add an existing account. Nothing is granted until the user accepts (then it becomes an
/// <see cref="OrganizationMembership"/> and this row goes away) — membership lets the org admin see the member's practice activity.
/// </summary>
public class OrganizationInvite
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public OrgRole Role { get; set; } = OrgRole.Member;

    public int? InvitedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One user's membership in one organization. <c>Admin</c> here is scoped strictly to this
/// organization's own data (see OrgAdminController) — separate from the platform-wide
/// Admin:Emails/User.IsAdmin super-admin, who can see every organization.
/// </summary>
public class OrganizationMembership
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public OrgRole Role { get; set; } = OrgRole.Member;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
