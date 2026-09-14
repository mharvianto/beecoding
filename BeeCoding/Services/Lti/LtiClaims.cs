namespace BeeCoding.Services.Lti;

/// <summary>The LTI 1.3 claim URIs this app reads out of a launch id_token.</summary>
public static class LtiClaims
{
    public const string MessageType = "https://purl.imsglobal.org/spec/lti/claim/message_type";
    public const string Version = "https://purl.imsglobal.org/spec/lti/claim/version";
    public const string DeploymentId = "https://purl.imsglobal.org/spec/lti/claim/deployment_id";
    public const string TargetLinkUri = "https://purl.imsglobal.org/spec/lti/claim/target_link_uri";
    public const string ResourceLink = "https://purl.imsglobal.org/spec/lti/claim/resource_link";
    public const string Context = "https://purl.imsglobal.org/spec/lti/claim/context";
    public const string Roles = "https://purl.imsglobal.org/spec/lti/claim/roles";
    public const string Ags = "https://purl.imsglobal.org/spec/lti-ags/claim/endpoint";
    public const string DeepLinkingSettings = "https://purl.imsglobal.org/spec/lti-dl/claim/deep_linking_settings";
    public const string DeepLinkingContentItems = "https://purl.imsglobal.org/spec/lti-dl/claim/content_items";
    public const string DeepLinkingData = "https://purl.imsglobal.org/spec/lti-dl/claim/data";
    public const string ForUser = "https://purl.imsglobal.org/spec/lti/claim/for_user";

    public const string MessageTypeResourceLink = "LtiResourceLinkRequest";
    public const string MessageTypeDeepLinking = "LtiDeepLinkingRequest";
    public const string MessageTypeSubmissionReview = "LtiSubmissionReviewRequest";

    /// <summary>Any of these role URNs (case-insensitive substring on the last segment)
    /// counts as course staff — granted a Board's Teacher membership, not Student.</summary>
    public static bool IsInstructor(IEnumerable<string> roles) =>
        roles.Any(r => r.Contains("Instructor", StringComparison.OrdinalIgnoreCase)
            || r.Contains("ContentDeveloper", StringComparison.OrdinalIgnoreCase)
            || r.Contains("Administrator", StringComparison.OrdinalIgnoreCase));
}
