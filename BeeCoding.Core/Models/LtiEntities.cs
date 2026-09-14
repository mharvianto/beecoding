using System.ComponentModel.DataAnnotations;

namespace BeeCoding.Models;

/// <summary>
/// An LMS registered as an LTI 1.3 Platform against this app (the Tool). Admin-managed —
/// see AdminUiController's lti-platforms endpoints. Values here mirror exactly what the
/// platform publishes when BeeCoding is registered as an external tool: issuer, the
/// client_id it assigned us, its OIDC auth endpoint, its token endpoint (for AGS/NRPS
/// service calls), and its public-key (JWKS) endpoint (to verify launch id_tokens).
/// </summary>
public class LtiPlatform
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";

    /// <summary>The platform's `iss` claim.</summary>
    [MaxLength(300)]
    public string Issuer { get; set; } = "";

    /// <summary>The client_id the platform assigned to this tool.</summary>
    [MaxLength(200)]
    public string ClientId { get; set; } = "";

    /// <summary>Comma-separated `lti_deployment_id` values — a registration can cover
    /// more than one deployment (e.g. separate Moodle tenants sharing one client_id).</summary>
    [MaxLength(500)]
    public string DeploymentIds { get; set; } = "";

    [MaxLength(500)]
    public string AuthLoginUrl { get; set; } = "";

    [MaxLength(500)]
    public string AuthTokenUrl { get; set; } = "";

    [MaxLength(500)]
    public string JwksUrl { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Which organization this LMS belongs to, if any. When set, every launch
    /// through this platform auto-joins the launching user to the organization, and every
    /// board this platform auto-creates is assigned to it — see LtiProvisioningService.</summary>
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Links a platform's stable per-user `sub` claim to a BeeCoding account —
/// created on first launch (auto-provisioned), reused on every later launch.</summary>
public class LtiUserLink
{
    public int Id { get; set; }

    public int LtiPlatformId { get; set; }
    public LtiPlatform? LtiPlatform { get; set; }

    [MaxLength(255)]
    public string Subject { get; set; } = "";

    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One specific LTI "activity" placement (a resource_link in a course context on a
/// deployment) mapped to a Board. Created the first time an instructor launches a brand
/// new placement (or via the Deep Linking picker), then reused by every later launch —
/// student or instructor — of that same placement, so everyone in the course lands on
/// the same board.
/// </summary>
public class LtiResourceLink
{
    public int Id { get; set; }

    public int LtiPlatformId { get; set; }
    public LtiPlatform? LtiPlatform { get; set; }

    [MaxLength(255)]
    public string DeploymentId { get; set; } = "";

    [MaxLength(255)]
    public string ContextId { get; set; } = "";

    [MaxLength(255)]
    public string ResourceLinkId { get; set; } = "";

    public int? BoardId { get; set; }
    public Board? Board { get; set; }

    /// <summary>Set instead of <see cref="BoardId"/> when the teacher picked a single
    /// Practice/bank problem in the Deep Linking picker rather than a whole board —
    /// exactly one of the two is ever set.</summary>
    public int? BankProblemId { get; set; }
    public BankProblem? BankProblem { get; set; }

    /// <summary>AGS line-item URL for this placement, captured from the most recent
    /// launch's endpoint claim — null if the platform never granted grading scope here.</summary>
    [MaxLength(500)]
    public string? LineItemUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Singleton row (Id=1): the RSA key pair BeeCoding signs its own LTI messages
/// with (Deep Linking responses, and the JWT-bearer client assertion used to fetch an
/// AGS access token). Generated once at first use and kept stable — the platform caches
/// our JWKS by `kid`, so rotating this silently would break signature verification until
/// its cache expires.</summary>
public class LtiToolKey
{
    public int Id { get; set; } = 1;

    [MaxLength(64)]
    public string KeyId { get; set; } = "";

    /// <summary>RSA private key, PKCS8 PEM.</summary>
    public string PrivateKeyPem { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
