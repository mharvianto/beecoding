using BeeCoding.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BeeCoding.Services;

/// <summary>
/// Passkeys (WebAuthn) as a passwordless way to sign in: "Sign in with a passkey" needs no email or password.
/// Credentials are discoverable and always need user verification (biometrics / device PIN), so a passkey
/// sign-in is already two factors and skips the authenticator-app step. Off unless <c>Auth:Passkeys:RpId</c>
/// is set: passkeys are bound to a domain, so they need HTTPS and a real host name (localhost works for
/// development). Config: <c>RpId</c> (the registrable domain), <c>RpName</c> (shown by the authenticator),
/// <c>Origins</c> (allowed page origins; defaults to <c>https://{RpId}</c>).
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

    /// <summary>The user handle stored in the passkey: it must map back to the account at sign-in.</summary>
    public static byte[] UserHandle(int userId) => BitConverter.GetBytes(userId);

    public CredentialCreateOptions CreationOptions(User user, IEnumerable<byte[]> existingIds) =>
        _fido2!.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = UserHandle(user.Id), Name = user.Email, DisplayName = user.DisplayName },
            ExcludeCredentials = existingIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,   // discoverable: the browser can offer it without an email
                UserVerification = UserVerificationRequirement.Required,
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

    /// <summary>A challenge with no allow-list: the browser shows whichever passkeys it has for this site.</summary>
    public AssertionOptions LoginOptions() =>
        _fido2!.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = new List<PublicKeyCredentialDescriptor>(),
            UserVerification = UserVerificationRequirement.Required,
        });

    public Task<VerifyAssertionResult> CompleteLoginAsync(
        AuthenticatorAssertionRawResponse response, AssertionOptions options, UserPasskey stored) =>
        _fido2!.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = response,
            OriginalOptions = options,
            StoredPublicKey = stored.PublicKey,
            StoredSignatureCounter = (uint)stored.SignCount,
            // the handle the authenticator returns must be the one this credential was created for
            IsUserHandleOwnerOfCredentialIdCallback = (p, _) => Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(UserHandle(stored.UserId))),
        });
}
