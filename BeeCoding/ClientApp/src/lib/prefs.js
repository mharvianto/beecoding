// Display preferences that follow the user across devices.
//
// The app keeps every preference in localStorage (theme, editor font, default language, ...) and reads it at
// start-up, so the code that uses them stays unchanged. This module mirrors a fixed set of those keys to the
// server (PreferencesController, same allowlist):
//   - signed-in writes to a synced key are pushed to the server shortly after (debounced);
//   - at start-up, before the app loads, the server's values are copied into localStorage;
//   - after signing in during the session, server values that differ from this browser's cause one reload,
//     because modules such as the theme read their value only once.
// Anything not listed here (code drafts, celebration markers, ...) stays per-device on purpose.
import { withBase } from './base';

export const SYNCED_KEYS = [
  'beecoding.theme', 'beecoding.tableView', 'beecoding.boardView', 'beecoding.playground.focus',
  'beecoding.lang', 'beecoding.aiLang',
  'beecoding.editor.theme', 'beecoding.editor.fontFamily', 'beecoding.editor.fontSize',
  'beecoding.editor.customTheme', 'beecoding.editor.customThemeName', 'beecoding.editor.cppFormatStyle', 'beecoding.editor.lsp',
  'beecoding.account.engagementGranularity',
  'beecoding.board.stats.engagementGranularity', 'beecoding.board.stats.aiGranularity',
  'beecoding.orgAdmin.engagementGranularity', 'beecoding.orgAdmin.aiGranularity',
  'beecoding.admin.engagementGranularity', 'beecoding.admin.aiGranularity',
];
const SYNCED = new Set(SYNCED_KEYS);
const OWNER_KEY = 'beecoding.prefs.owner';   // whose preferences this browser's copy currently holds
const DEBOUNCE_MS = 800;

let active = false;      // a user is signed in and synced: local writes are mirrored
let applying = false;    // we are writing server values into localStorage: don't echo them back
let syncedUser = null;
const pending = {};
let timer = null;

const read = (k) => { try { return localStorage.getItem(k); } catch { return null; } };

async function call(method, body, timeoutMs = 4000) {
  const ctl = new AbortController();
  const t = setTimeout(() => ctl.abort(), timeoutMs);
  try {
    const res = await fetch(withBase('/api/me/preferences'), {
      method, credentials: 'include', signal: ctl.signal,
      headers: body ? { 'Content-Type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined,
    });
    return res.ok ? await res.json() : null;   // 401 (signed out) / 403 (held for 2-step setup) / errors: just skip
  } catch { return null; } finally { clearTimeout(t); }
}

async function flush() {
  timer = null;
  const values = { ...pending };
  for (const k of Object.keys(pending)) delete pending[k];
  if (Object.keys(values).length) await call('PUT', { values });
}

function queue(key, value) {
  if (!active) return;
  pending[key] = value;
  clearTimeout(timer);
  timer = setTimeout(flush, DEBOUNCE_MS);
}

// Mirror writes to synced keys. Wrapping Storage.prototype (checked against localStorage) keeps every existing
// call site untouched.
(function patchStorage() {
  const proto = Storage.prototype;
  const set = proto.setItem, remove = proto.removeItem;
  proto.setItem = function (k, v) {
    set.call(this, k, v);
    if (this === window.localStorage && !applying && SYNCED.has(k)) queue(k, String(v));
  };
  proto.removeItem = function (k) {
    remove.call(this, k);
    if (this === window.localStorage && !applying && SYNCED.has(k)) queue(k, null);
  };
})();

/** Make this browser agree with the server. Returns true if any local value was replaced. */
async function reconcile() {
  const server = await call('GET');
  if (!server) return { ok: false, changed: false };
  active = true;
  syncedUser = server.userId;

  const sameOwner = read(OWNER_KEY) === String(server.userId);
  const firstEver = read(OWNER_KEY) === null;
  const serverEmpty = Object.keys(server.values).length === 0;
  let changed = false;

  applying = true;
  try {
    if (!sameOwner && !firstEver) {
      // Another person used this browser: drop their values so they don't leak into this account.
      for (const k of SYNCED_KEYS) { if (read(k) !== null) { localStorage.removeItem(k); changed = true; } }
    }
    for (const [k, v] of Object.entries(server.values)) {
      if (SYNCED.has(k) && read(k) !== v) { localStorage.setItem(k, v); changed = true; }
    }
    localStorage.setItem(OWNER_KEY, String(server.userId));
  } catch { /* storage unavailable */ } finally { applying = false; }

  // This browser's own values go up when the account has none yet (first sign-in after this feature shipped),
  // and for any key the server lacks while the same person has been using this browser.
  if (serverEmpty || sameOwner || firstEver) {
    const up = {};
    for (const k of SYNCED_KEYS) { const v = read(k); if (v !== null && server.values[k] === undefined) up[k] = v; }
    if (Object.keys(up).length) await call('PUT', { values: up });
  }
  return { ok: true, changed };
}

/** Run before the app loads, so modules that read localStorage once see the server's values. */
export async function bootstrapPrefs() {
  await reconcile();
}

/** Call when a user becomes signed in during the session (login, register...). May reload once. */
export async function onSignedIn(userId) {
  if (syncedUser === userId) return;   // bootstrap already did it
  const { ok, changed } = await reconcile();
  if (ok && changed) window.location.reload();
}

export function onSignedOut() {
  active = false;
  syncedUser = null;
  clearTimeout(timer);
  timer = null;
  for (const k of Object.keys(pending)) delete pending[k];
}
