using BeeCoding.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BeeCoding.Services;

/// <summary>
/// WebAuthn (passkeys / security keys) as a second sign-in factor. Off unless <c>Auth:Passkeys:RpId</c>
/// is set: passkeys are bound to a domain, so they need HTTPS and a real host name (localhost works
/// for development). Config: <c>RpId</c> (the registrable domain), <c>RpName</c> (shown by the
/// authenticator), <c>Origins</c> (allowed page origins; defaults to <c>https://{RpId}</c>).
/// </summary>
public class PasskeyService
{
    private readonly IFido2? _fido2;

    public PasskeyService(IConfiguration cfg)
    {
        var rpId = cfg["Auth:Passkeys:RpId"]?.Trim();
        if (string.IsNullOrEmpty(rpId)) return;

        var origins = cfg.GetSection("Auth:Passkeys:Origins").GetChildren().Select(c => c.Value)
            .Concat((cfg["Auth:Passkeys:Origins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(o => o?.Trim().TrimEnd('/')).Where(o => !string.IsNullOrEmpty(o)).Select(o => o!).ToHashSet();
        if (origins.Count == 0) origins.Add($"https://{rpId}");

        _fido2 = new Fido2(new Fido2Configuration
        {
            ServerDomain = rpId,
            ServerName = string.IsNullOrWhiteSpace(cfg["Auth:Passkeys:RpName"]) ? "BeeCoding" : cfg["Auth:Passkeys:RpName"]!,
            Origins = origins,
        });
    }

    public bool Enabled => _fido2 is not null;

    public CredentialCreateOptions CreationOptions(User user, IEnumerable<byte[]> existingIds) =>
        _fido2!.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = BitConverter.GetBytes(user.Id), Name = user.Email, DisplayName = user.DisplayName },
            ExcludeCredentials = existingIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

    public Task<RegisteredPublicKeyCredential> CompleteRegistrationAsync(
        AuthenticatorAttestationRawResponse response, CredentialCreateOptions options, Func<byte[], Task<bool>> isUnique) =>
        _fido2!.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = (p, _) => isUnique(p.CredentialId),
        });

    public AssertionOptions AssertionOptions(IEnumerable<byte[]> credentialIds) =>
        _fido2!.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            UserVerification = UserVerificationRequirement.Preferred,
        });

    public Task<VerifyAssertionResult> CompleteAssertionAsync(
        AuthenticatorAssertionRawResponse response, AssertionOptions options, UserPasskey stored) =>
        _fido2!.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = response,
            OriginalOptions = options,
            StoredPublicKey = stored.PublicKey,
            StoredSignatureCounter = (uint)stored.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (_, _) => Task.FromResult(true),   // not discoverable: the user is already known from the password step
        });
}
