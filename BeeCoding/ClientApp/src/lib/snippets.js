// The user's own code snippets: a prefix that expands to a body in the editor's autocomplete (and works even while
// clangd is off). Stored as JSON in localStorage like the other editor preferences, and synced to the account
// (see lib/prefs.js and PreferencesController). The body uses Monaco/VS Code snippet syntax: $1, ${1:name}, $0.
import { ref } from 'vue';
import * as monaco from 'monaco-editor';

const KEY = 'beecoding.editor.snippets';
export const MAX_SNIPPETS = 40;
export const MAX_BODY_CHARS = 1500;
export const PREFIX_RE = /^[A-Za-z_][A-Za-z0-9_]{0,23}$/;
export const SNIPPET_LANGS = [{ id: '', label: 'C and C++' }, { id: 'c', label: 'C' }, { id: 'cpp', label: 'C++' }];

const valid = (s) => s && typeof s.prefix === 'string' && PREFIX_RE.test(s.prefix)
  && typeof s.body === 'string' && s.body.length > 0 && s.body.length <= MAX_BODY_CHARS;

function load() {
  try {
    const list = JSON.parse(localStorage.getItem(KEY) || '[]');
    return Array.isArray(list) ? list.filter(valid).slice(0, MAX_SNIPPETS).map((s) => ({
      prefix: s.prefix, body: s.body, description: String(s.description || '').slice(0, 80),
      lang: s.lang === 'c' || s.lang === 'cpp' ? s.lang : '',
    })) : [];
  } catch { return []; }
}

/** [{ prefix, body, description, lang: '' | 'c' | 'cpp' }] — shared by every editor on screen. */
export const userSnippets = ref(load());

export function saveSnippets(list) {
  userSnippets.value = list;
  try {
    if (list.length) localStorage.setItem(KEY, JSON.stringify(list)); else localStorage.removeItem(KEY);
  } catch { /* ignore */ }
}

let registered = false;
/** Register the suggestion provider once for the whole page; every editor shares it. */
export function ensureSnippetProvider() {
  if (registered) return;
  registered = true;
  for (const lang of ['c', 'cpp']) {
    monaco.languages.registerCompletionItemProvider(lang, {
      provideCompletionItems(model, position) {
        const w = model.getWordUntilPosition(position);
        const range = new monaco.Range(position.lineNumber, w.startColumn, position.lineNumber, w.endColumn);
        const id = model.getLanguageId();
        return {
          suggestions: userSnippets.value.filter((s) => !s.lang || s.lang === id).map((s) => ({
            label: s.prefix,
            kind: monaco.languages.CompletionItemKind.Snippet,
            detail: s.description || 'My snippet',
            documentation: { value: '```\n' + s.body + '\n```' },
            insertText: s.body,
            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
            filterText: s.prefix,
            sortText: '0_' + s.prefix,   // above clangd's items
            range,
          })),
        };
      },
    });
  }
}
