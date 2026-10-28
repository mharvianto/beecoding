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
  if (!force && (!browserNotifyWanted() || document.visibilityState !== 'hidden')) return;
  try {
    const n = new Notification(title, { body, tag: `beecoding-${id}`, icon: `${import.meta.env.BASE_URL}favicon.svg` });
    n.onclick = () => { window.focus(); n.close(); onClick?.(); };
  } catch { /* some mobile browsers only allow service-worker notifications */ }
}
