using System.IdentityModel.Tokens.Jwt;
using BeeCoding.Models;

namespace BeeCoding.Services.Lti;

/// <summary>Builds the signed JWT a Deep Linking response is sent as — a single
/// `ltiResourceLink` content item pointing at the board or bank problem the teacher
/// picked.</summary>
public class LtiDeepLinkService(LtiToolKeyService keys)
{
    private readonly LtiToolKeyService _keys = keys;

    public async Task<string> BuildResponseJwtAsync(
        LtiPlatform platform, string deploymentId, string? echoData, string title, string targetLinkUri)
    {
        var creds = await _keys.GetSigningCredentialsAsync();
        var now = DateTimeOffset.UtcNow;

        var contentItem = new Dictionary<string, object?>
        {
            ["type"] = "ltiResourceLink",
            ["title"] = title,
            ["url"] = targetLinkUri,
        };

        var payload = new JwtPayload
        {
            ["iss"] = platform.ClientId,
            ["aud"] = platform.Issuer,
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nonce"] = Guid.NewGuid().ToString("N"),
            [LtiClaims.MessageType] = "LtiDeepLinkingResponse",
            [LtiClaims.Version] = "1.3.0",
            [LtiClaims.DeploymentId] = deploymentId,
            [LtiClaims.DeepLinkingContentItems] = new object[] { contentItem },
        };
        if (echoData is not null) payload[LtiClaims.DeepLinkingData] = echoData;

        var header = new JwtHeader(creds);
        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
