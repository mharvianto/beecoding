// WebAuthn glue: the server (Fido2NetLib) speaks base64url JSON, the browser API wants ArrayBuffers.

export const passkeysSupported = () =>
  typeof window !== 'undefined' && !!window.PublicKeyCredential && !!navigator.credentials && window.isSecureContext;

const toBytes = (b64url) => {
  const pad = '='.repeat((4 - (b64url.length % 4)) % 4);
  const bin = atob(b64url.replace(/-/g, '+').replace(/_/g, '/') + pad);
  return Uint8Array.from(bin, (c) => c.charCodeAt(0));
};
const toB64url = (buf) => {
  const bytes = new Uint8Array(buf);
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
};
const descriptor = (d) => ({ ...d, id: toBytes(d.id) });

function explain(e) {
  if (e?.name === 'NotAllowedError') return new Error('The passkey request was cancelled or timed out.');
  if (e?.name === 'InvalidStateError') return new Error('This device already has a passkey registered for your account.');
  return e instanceof Error ? e : new Error(String(e));
}

/** Ask the authenticator to create a credential from the server's creation options. */
export async function createPasskey(options) {
  const publicKey = {
    ...options,
    challenge: toBytes(options.challenge),
    user: { ...options.user, id: toBytes(options.user.id) },
    excludeCredentials: (options.excludeCredentials || []).map(descriptor),
  };
  let cred;
  try { cred = await navigator.credentials.create({ publicKey }); } catch (e) { throw explain(e); }
  return {
    id: cred.id,
    rawId: toB64url(cred.rawId),
    type: cred.type,
    response: {
      attestationObject: toB64url(cred.response.attestationObject),
      clientDataJSON: toB64url(cred.response.clientDataJSON),
      transports: cred.response.getTransports ? cred.response.getTransports() : [],
    },
    clientExtensionResults: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {},
  };
}

/** Ask the authenticator to sign the server's login challenge with one of the allowed credentials. */
export async function getPasskey(options) {
  const publicKey = {
    ...options,
    challenge: toBytes(options.challenge),
    allowCredentials: (options.allowCredentials || []).map(descriptor),
  };
  let cred;
  try { cred = await navigator.credentials.get({ publicKey }); } catch (e) { throw explain(e); }
  return {
    id: cred.id,
    rawId: toB64url(cred.rawId),
    type: cred.type,
    response: {
      authenticatorData: toB64url(cred.response.authenticatorData),
      signature: toB64url(cred.response.signature),
      clientDataJSON: toB64url(cred.response.clientDataJSON),
      userHandle: cred.response.userHandle ? toB64url(cred.response.userHandle) : null,
    },
    clientExtensionResults: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {},
  };
}
