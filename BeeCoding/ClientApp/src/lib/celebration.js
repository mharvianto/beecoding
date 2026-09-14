// Tracks which submission id we've already shown confetti for, per user + problem — so a
// genuine first-time solve still celebrates even when discovered on reload/reopen (e.g. the
// tab was closed while grading finished, so the live SignalR push was missed) rather than
// only via the live event, and a later revisit to an already-solved problem never re-fires
// it. Best-effort — silently no-ops if localStorage is unavailable.

const key = (uid, scope) => `beecoding.celebrated.${uid || 0}.${scope}`;

export function alreadyCelebrated(uid, scope, submissionId) {
  try { return Number(localStorage.getItem(key(uid, scope))) >= submissionId; }
  catch { return false; }
}

export function markCelebrated(uid, scope, submissionId) {
  try { localStorage.setItem(key(uid, scope), String(submissionId)); }
  catch { /* quota exceeded / private mode — nothing we can do */ }
}
