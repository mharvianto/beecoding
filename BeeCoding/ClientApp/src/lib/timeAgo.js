const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
const STEPS = [[60, 'second'], [60, 'minute'], [24, 'hour'], [7, 'day'], [4.345, 'week'], [12, 'month'], [Infinity, 'year']];

// "3 days ago", "yesterday", in the browser's language. The server sends UTC without a trailing Z.
export function timeAgo(iso, now = Date.now()) {
  let n = (Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z') - now) / 1000;
  for (const [size, unit] of STEPS) {
    if (Math.abs(n) < size) return rtf.format(Math.round(n), unit);
    n /= size;
  }
}

export const fullTime = (iso) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z').toLocaleString();

// Line diff of two texts (longest common subsequence): [{t: ' ' | '+' | '-', s}] going from `a` to `b`.
// shortcut: O(n*m), so capped at 400 lines each, enough for a contest solution; a bigger file shows no diff.
export function lineDiff(a, b) {
  const x = a.split('\n'), y = b.split('\n');
  if (x.length > 400 || y.length > 400) return null;
  const L = Array.from({ length: x.length + 1 }, () => new Uint16Array(y.length + 1));
  for (let i = x.length - 1; i >= 0; i--)
    for (let j = y.length - 1; j >= 0; j--)
      L[i][j] = x[i] === y[j] ? L[i + 1][j + 1] + 1 : Math.max(L[i + 1][j], L[i][j + 1]);
  const out = []; let i = 0, j = 0;
  while (i < x.length && j < y.length) {
    if (x[i] === y[j]) { out.push({ t: ' ', s: x[i] }); i++; j++; }
    else if (L[i + 1][j] >= L[i][j + 1]) out.push({ t: '-', s: x[i++] });
    else out.push({ t: '+', s: y[j++] });
  }
  while (i < x.length) out.push({ t: '-', s: x[i++] });
  while (j < y.length) out.push({ t: '+', s: y[j++] });
  return out;
}
