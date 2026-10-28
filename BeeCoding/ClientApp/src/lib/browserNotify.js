// Optional desktop notifications for the bell: shown only while the tab is in the background (a visible tab
// already gets the in-app toast). The on/off choice is a synced preference; the browser's permission is per device.
const KEY = 'beecoding.notify.browser';

export const browserNotifySupported = () => typeof window !== 'undefined' && 'Notification' in window;
export const browserNotifyPermission = () => (browserNotifySupported() ? Notification.permission : 'unsupported');

export function browserNotifyWanted() {
  try { return localStorage.getItem(KEY) === '1'; } catch { return false; }
}

/** Turn the preference on or off. Turning on asks for permission; resolves to 'on', 'off' or 'denied'. */
export async function setBrowserNotify(on) {
  try {
    if (!on) { localStorage.setItem(KEY, '0'); return 'off'; }
    if (!browserNotifySupported()) return 'denied';
    const perm = Notification.permission === 'default' ? await Notification.requestPermission() : Notification.permission;
    if (perm !== 'granted') return 'denied';
    localStorage.setItem(KEY, '1');
    return 'on';
  } catch { return 'denied'; }
}

/** Show a notification if the user opted in, permission is granted and the tab is in the background (`force` skips the opt-in and tab checks: the Account test button). */
export function showBrowserNotification({ id, title, body, force = false }, onClick) {
  if (browserNotifyPermission() !== 'granted') return;
  if (!force && (!browserNotifyWanted() || pushDeviceOn() || document.visibilityState !== 'hidden')) return;
  try {
    const n = new Notification(title, { body, tag: `beecoding-${id}`, icon: `${import.meta.env.BASE_URL}favicon.svg` });
    n.onclick = () => { window.focus(); n.close(); onClick?.(); };
  } catch { /* some mobile browsers only allow service-worker notifications */ }
}

// ---- Web Push (works with the tab closed; needs the server's VAPID keys) ----
const DEVICE_KEY = 'beecoding.push.device';   // per device, deliberately not a synced preference
const swUrl = () => `${import.meta.env.BASE_URL}sw.js`;

export const pushSupported = () => typeof navigator !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window && browserNotifySupported();
export function pushDeviceOn() {
  try { return localStorage.getItem(DEVICE_KEY) === '1'; } catch { return false; }
}
const setDevice = (on) => { try { localStorage.setItem(DEVICE_KEY, on ? '1' : '0'); } catch { /* ignore */ } };

const keyBytes = (b64) => {
  const raw = atob((b64 + '='.repeat((4 - (b64.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(raw, (c) => c.charCodeAt(0));
};

async function registration() {
  return (await navigator.serviceWorker.getRegistration(import.meta.env.BASE_URL))
    || navigator.serviceWorker.register(swUrl(), { scope: import.meta.env.BASE_URL });
}

/** Server-side switch: { enabled, publicKey } or null when it can't be read. */
export async function pushConfig(api) {
  try { return await api.get('/api/push/config'); } catch { return null; }
}

/** Subscribe this browser; resolves to 'on' or 'denied'. */
export async function subscribePush(api, publicKey) {
  try {
    if (!pushSupported()) return 'denied';
    const perm = Notification.permission === 'default' ? await Notification.requestPermission() : Notification.permission;
    if (perm !== 'granted') return 'denied';
    const reg = await registration();
    await navigator.serviceWorker.ready;
    const sub = (await reg.pushManager.getSubscription())
      || await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(publicKey) });
    const j = sub.toJSON();
    await api.post('/api/me/push', { endpoint: j.endpoint, p256dh: j.keys.p256dh, auth: j.keys.auth });
    setDevice(true);
    return 'on';
  } catch { return 'denied'; }
}

export async function unsubscribePush(api) {
  setDevice(false);
  try {
    const reg = await navigator.serviceWorker.getRegistration(import.meta.env.BASE_URL);
    const sub = await reg?.pushManager.getSubscription();
    if (!sub) return;
    await api.post('/api/me/push/unsubscribe', { endpoint: sub.endpoint }).catch(() => {});
    await sub.unsubscribe();
  } catch { /* ignore */ }
}

/** On sign-out: take this browser off the account's push list but keep the browser subscription, so the next sign-in just re-attaches it. */
export async function detachPush(api) {
  if (!pushDeviceOn() || !pushSupported()) return;
  try {
    const reg = await navigator.serviceWorker.getRegistration(import.meta.env.BASE_URL);
    const sub = await reg?.pushManager.getSubscription();
    if (sub) await api.post('/api/me/push/unsubscribe', { endpoint: sub.endpoint });
  } catch { /* ignore */ }
}

/** After sign-in on a subscribed device: point the subscription at the current account (a shared browser may have had another user). */
export async function refreshPush(api) {
  if (!pushDeviceOn() || !pushSupported() || Notification.permission !== 'granted') return;
  try {
    const reg = await navigator.serviceWorker.getRegistration(import.meta.env.BASE_URL);
    const j = (await reg?.pushManager.getSubscription())?.toJSON();
    if (j) await api.post('/api/me/push', { endpoint: j.endpoint, p256dh: j.keys.p256dh, auth: j.keys.auth });
  } catch { /* ignore */ }
}
