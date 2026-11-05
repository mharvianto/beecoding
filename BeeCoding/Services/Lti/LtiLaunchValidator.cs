using System.IdentityModel.Tokens.Jwt;
using BeeCoding.Models;
using Microsoft.IdentityModel.Tokens;

namespace BeeCoding.Services.Lti;

public class LtiLaunchValidator(LtiJwksCache jwks)
{
    private readonly LtiJwksCache _jwks = jwks;

    /// <summary>Verifies signature (against the platform's JWKS), issuer, audience,
    /// lifetime, and nonce (replay protection — the nonce must match the one this app
    /// generated for the login it initiated). Returns the decoded token on success.</summary>
    public async Task<(JwtSecurityToken? Token, string? Error)> ValidateAsync(
        string idToken, LtiPlatform platform, string expectedNonce, CancellationToken ct = default)
    {
        var handler = new JwtSecurityTokenHandler();
        async Task<(JwtSecurityToken? Jwt, string? Error, bool KeyProblem)> TryAsync(bool refresh)
        {
            JsonWebKeySet keys;
            try { keys = await _jwks.GetAsync(platform.JwksUrl, refresh, ct); }
            catch (Exception ex) { return (null, $"couldn't fetch the platform's JWKS: {ex.Message}", false); }

            var validationParams = new TokenValidationParameters
            {
                ValidIssuer = platform.Issuer,
                ValidAudience = platform.ClientId,
                IssuerSigningKeys = keys.GetSigningKeys(),
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };
            try
            {
                handler.ValidateToken(idToken, validationParams, out var validated);
                return ((JwtSecurityToken)validated, null, false);
            }
            catch (Exception ex) { return (null, $"invalid id_token: {ex.Message}", ex is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException); }
        }

        var (jwt, error, keyProblem) = await TryAsync(false);
        if (jwt is null && keyProblem) (jwt, error, _) = await TryAsync(true);   // key rotated since we cached the set
        if (jwt is null) return (null, error);

        var nonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (string.IsNullOrEmpty(nonce) || nonce != expectedNonce)
            return (null, "nonce mismatch — possible replay");

        return (jwt, null);
    }
}
