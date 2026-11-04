import { ref } from 'vue';

// Language of the long-form pages (privacy policy, terms, the problem-writing guide): English or Indonesian.
// English is the default. A synced preference (see lib/prefs.js), so it follows the user across devices.
const KEY = 'beecoding.docLang';

function read() {
  try { return localStorage.getItem(KEY) === 'id' ? 'id' : 'en'; } catch { return 'en'; }
}

export const docLang = ref(read());

export function setDocLang(lang) {
  docLang.value = lang === 'id' ? 'id' : 'en';
  try { localStorage.setItem(KEY, docLang.value); } catch { /* the choice just does not persist */ }
}
