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
