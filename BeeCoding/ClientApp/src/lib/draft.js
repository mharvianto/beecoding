// Local autosave of the code editor so an accidental refresh / tab close doesn't lose work.
// Keyed per user + problem. Best-effort — silently no-ops if localStorage is unavailable.

const key = (uid, scope) => `beecoding.draft.${uid || 0}.${scope}`;
const INDEX = 'beecoding.draft.index';
const MAX_DRAFTS = 50;
const DAY_MS = 24 * 60 * 60 * 1000;
const WEEK_MS = 7 * DAY_MS;
const SOLVED_MS = 60 * 60 * 1000;   // a solved problem's code is on the server already

export function loadDraft(uid, scope) {
  try {
    const raw = localStorage.getItem(key(uid, scope));
    if (!raw) return null;
    const d = JSON.parse(raw);
    return d && typeof d.code === 'string' ? d : null;   // { code, lang, ts }
  } catch {
    return null;
  }
}

export function saveDraft(uid, scope, code, lang) {
  try {
    const k = key(uid, scope);
    localStorage.setItem(k, JSON.stringify({ code, lang, ts: Date.now() }));
    touchIndex(k);
  } catch {
    /* quota exceeded / private mode — nothing we can do */
  }
}

// Marks a draft as solved so the retention pass below can expire it sooner.
export function markDraftAccepted(uid, scope) {
  try {
    const k = key(uid, scope);
    const raw = localStorage.getItem(k);
    if (!raw) return;
    const d = JSON.parse(raw);
    if (!d || typeof d.code !== 'string') return;
    d.acAt = Date.now();
    localStorage.setItem(k, JSON.stringify(d));
  } catch {
    /* ignore */
  }
}

// Retention pass: drop drafts for already-solved problems after 1 hour, unsolved after 1
// week. Meant to run once per app load, not on every save.
export function pruneDrafts() {
  try {
    const idx = JSON.parse(localStorage.getItem(INDEX) || '[]');
    const now = Date.now();
    const kept = [];
    for (const k of idx) {
      const raw = localStorage.getItem(k);
      if (!raw) continue;
      let d;
      try { d = JSON.parse(raw); } catch { localStorage.removeItem(k); continue; }
      const expired = d?.acAt ? now - d.acAt > SOLVED_MS : now - (d?.ts || 0) > WEEK_MS;
      if (expired) localStorage.removeItem(k);
      else kept.push(k);
    }
    localStorage.setItem(INDEX, JSON.stringify(kept));
  } catch {
    /* ignore */
  }
}

// Sign-out / account deletion: the computer may be shared, so nothing of the person's code stays behind.
export function clearAllDrafts() {
  try {
    for (const k of JSON.parse(localStorage.getItem(INDEX) || '[]')) localStorage.removeItem(k);
    localStorage.removeItem(INDEX);
    // drafts whose index entry was lost (the index is capped) are found by prefix
    for (let i = localStorage.length - 1; i >= 0; i--) {
      const k = localStorage.key(i);
      if (k?.startsWith('beecoding.draft.')) localStorage.removeItem(k);
    }
  } catch {
    /* ignore */
  }
}

export function clearDraft(uid, scope) {
  try {
    const k = key(uid, scope);
    localStorage.removeItem(k);
    let idx = JSON.parse(localStorage.getItem(INDEX) || '[]');
    idx = idx.filter((x) => x !== k);
    localStorage.setItem(INDEX, JSON.stringify(idx));
  } catch {
    /* ignore */
  }
}

// keep the newest MAX_DRAFTS entries, drop the rest
function touchIndex(k) {
  try {
    let idx = JSON.parse(localStorage.getItem(INDEX) || '[]');
    idx = idx.filter((x) => x !== k);
    idx.push(k);
    while (idx.length > MAX_DRAFTS) localStorage.removeItem(idx.shift());
    localStorage.setItem(INDEX, JSON.stringify(idx));
  } catch {
    /* ignore */
  }
}
