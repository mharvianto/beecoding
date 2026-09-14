using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace BeeCoding.Services.Lti;

/// <summary>Flattened, typed view of the LTI claims inside a validated launch id_token.
/// JwtSecurityToken exposes nested claim objects (context, resource_link, ags endpoint,
/// deep_linking_settings) as raw JSON, so this re-parses the payload once via
/// System.Text.Json rather than repeating that at every call site.</summary>
public sealed class LtiLaunchClaims
{
    public required string MessageType { get; init; }
    public required string Issuer { get; init; }
    public required string Subject { get; init; }
    public required string DeploymentId { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public List<string> Roles { get; init; } = new();
    public string? ContextId { get; init; }
    public string? ContextTitle { get; init; }
    public string? ResourceLinkId { get; init; }
    public string? ResourceLinkTitle { get; init; }
    public string? AgsLineItemUrl { get; init; }
    public List<string> AgsScopes { get; init; } = new();
    public string? DeepLinkReturnUrl { get; init; }
    public string? DeepLinkData { get; init; }
    public string? TargetLinkUri { get; init; }
    /// <summary>Only present on a Submission Review launch (message_type
    /// LtiSubmissionReviewRequest) — the platform `sub` of the student whose work an
    /// instructor clicked "review" on, e.g. from the LMS gradebook.</summary>
    public string? ForUserId { get; init; }

    public static LtiLaunchClaims Parse(JwtSecurityToken jwt)
    {
        using var doc = JsonDocument.Parse(jwt.Payload.SerializeToJson());
        var root = doc.RootElement;
        string? Str(JsonElement el, string key) => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string? RootStr(string key) => Str(root, key);

        var roles = new List<string>();
        if (root.TryGetProperty(LtiClaims.Roles, out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
            foreach (var r in rolesEl.EnumerateArray()) if (r.GetString() is { } s) roles.Add(s);

        string? contextId = null, contextTitle = null;
        if (root.TryGetProperty(LtiClaims.Context, out var ctxEl) && ctxEl.ValueKind == JsonValueKind.Object)
        {
            contextId = Str(ctxEl, "id");
            contextTitle = Str(ctxEl, "title");
        }

        string? rlId = null, rlTitle = null;
        if (root.TryGetProperty(LtiClaims.ResourceLink, out var rlEl) && rlEl.ValueKind == JsonValueKind.Object)
        {
            rlId = Str(rlEl, "id");
            rlTitle = Str(rlEl, "title");
        }

        string? agsLineItem = null;
        var agsScopes = new List<string>();
        if (root.TryGetProperty(LtiClaims.Ags, out var agsEl) && agsEl.ValueKind == JsonValueKind.Object)
        {
            agsLineItem = Str(agsEl, "lineitem");
            if (agsEl.TryGetProperty("scope", out var scopeEl) && scopeEl.ValueKind == JsonValueKind.Array)
                foreach (var s in scopeEl.EnumerateArray()) if (s.GetString() is { } sv) agsScopes.Add(sv);
        }

        string? dlReturn = null, dlData = null;
        if (root.TryGetProperty(LtiClaims.DeepLinkingSettings, out var dlEl) && dlEl.ValueKind == JsonValueKind.Object)
        {
            dlReturn = Str(dlEl, "deep_link_return_url");
            dlData = Str(dlEl, "data");
        }

        string? forUserId = null;
        if (root.TryGetProperty(LtiClaims.ForUser, out var fuEl) && fuEl.ValueKind == JsonValueKind.Object)
            forUserId = Str(fuEl, "user_id");

        return new LtiLaunchClaims
        {
            MessageType = RootStr(LtiClaims.MessageType) ?? "",
            Issuer = jwt.Issuer,
            Subject = jwt.Subject ?? "",
            DeploymentId = RootStr(LtiClaims.DeploymentId) ?? "",
            Email = RootStr("email"),
            Name = RootStr("name"),
            Roles = roles,
            ContextId = contextId,
            ContextTitle = contextTitle,
            ResourceLinkId = rlId,
            ResourceLinkTitle = rlTitle,
            AgsLineItemUrl = agsLineItem,
            AgsScopes = agsScopes,
            DeepLinkReturnUrl = dlReturn,
            DeepLinkData = dlData,
            TargetLinkUri = RootStr(LtiClaims.TargetLinkUri),
            ForUserId = forUserId,
        };
    }
}
