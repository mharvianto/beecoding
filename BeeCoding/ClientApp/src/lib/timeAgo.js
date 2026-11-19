import { docLang } from './docLang';
import { tr } from './i18n';

const STEPS = [[60, 'second'], [60, 'minute'], [24, 'hour'], [7, 'day'], [4.345, 'week'], [12, 'month'], [Infinity, 'year']];
const parse = (iso) => Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z');   // the server sends UTC without a trailing Z

// "3 days ago", "yesterday", in the app's language.
export function timeAgo(iso, now = Date.now()) {
  const rtf = new Intl.RelativeTimeFormat(docLang.value, { numeric: 'auto' });
  let n = (parse(iso) - now) / 1000;
  for (const [size, unit] of STEPS) {
    if (Math.abs(n) < size) return rtf.format(Math.round(n), unit);
    n /= size;
  }
}

// Compact for tight spots: "just now", "7m ago", "3h ago", "2d ago". Empty for a missing or unreadable time.
export function agoShort(iso, now = Date.now()) {
  const t = iso ? parse(iso) : NaN;
  if (!t) return '';
  const s = Math.max(1, Math.floor((now - t) / 1000));
  if (s < 60) return tr('just now');
  if (s < 3600) return tr('{n}m ago', { n: Math.floor(s / 60) });
  if (s < 86400) return tr('{n}h ago', { n: Math.floor(s / 3600) });
  return tr('{n}d ago', { n: Math.floor(s / 86400) });
}

export const fullTime = (iso) => new Date(parse(iso)).toLocaleString(docLang.value === 'id' ? 'id-ID' : undefined);
