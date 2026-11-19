import { watch } from 'vue';
import { docLang } from './docLang';
import id from './i18n/id.js';

// UI translation. The English text IS the key: tr('Save') is 'Save' in English and id['Save'] in Indonesian, and a text
// with no entry simply stays English, so a missing translation never breaks a screen. `{name}` in a text is replaced
// from `params`. The language is the same one the legal pages and the guide use (lib/docLang.js, a synced preference).
// `docLang` is read inside tr(), so every template that calls it re-renders when the language changes.
// Texts the server sends with a value inside ("Please wait 3s before submitting again.") cannot match a key exactly, so an
// entry written with {p0}, {p1}... in it also works as a pattern; the numbers it catches fill the Indonesian text.
const patterns = Object.keys(id).filter((k) => /\{p\d+\}/.test(k)).map((k) => {
  const re = k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\\\{p(\d+)\\\}/g, '(.+?)');
  return [new RegExp(`^${re}$`, 's'), id[k], [...k.matchAll(/\{p(\d+)\}/g)].map((m) => m[1])];
});
const seen = new Map();   // server text -> its Indonesian form, so a repeated message is matched once

function viaPattern(text) {
  if (seen.has(text)) return seen.get(text);
  let out = null;
  for (const [re, to, names] of patterns) {
    const m = re.exec(text);
    if (m) { out = to; names.forEach((n, i) => { out = out.replaceAll(`{p${n}}`, m[i + 1]); }); break; }
  }
  if (seen.size > 500) seen.clear();
  seen.set(text, out);
  return out;
}

export function tr(text, params) {
  let s = text;
  if (docLang.value === 'id' && typeof text === 'string') s = id[text] || viaPattern(text) || text;
  if (params) for (const k in params) s = s.replaceAll(`{${k}}`, params[k]);
  return s;
}

watch(docLang, (l) => { document.documentElement.lang = l; }, { immediate: true });
