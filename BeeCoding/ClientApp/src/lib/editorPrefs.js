// Editor theme + font family preferences — deliberately NOT toolbar buttons (there are
// already enough of those). Only reachable via Monaco's built-in command palette (F1 /
// Ctrl+Shift+F1), same way the LSP toggle is. Shared across every MonacoEditor instance on
// screen (Vue refs imported from a module are singletons, same pattern as lib/theme.js).
import { ref } from 'vue';
import * as monaco from 'monaco-editor';

const THEME_KEY = 'beecoding.editor.theme';
const FONT_KEY = 'beecoding.editor.fontFamily';
const CPP_FORMAT_KEY = 'beecoding.editor.cppFormatStyle';

/** 'auto' (follow site light/dark) | a Monaco theme id (built-in or custom-defined below) */
export const editorThemePref = ref(localStorage.getItem(THEME_KEY) || 'auto');
/** '' (Monaco's built-in default stack) | a CSS font-family value */
export const editorFontFamily = ref(localStorage.getItem(FONT_KEY) || '');
/** clang-format BasedOnStyle for "Format Document" on C/C++ — must match
 * ClangdSession.FormatStyles on the server (an allowlist, not free text). */
export const cppFormatStyle = ref(localStorage.getItem(CPP_FORMAT_KEY) || 'LLVM');

export function setEditorTheme(id) {
  editorThemePref.value = id;
  try { localStorage.setItem(THEME_KEY, id); } catch { /* ignore */ }
}
export function setEditorFontFamily(family) {
  editorFontFamily.value = family;
  try { localStorage.setItem(FONT_KEY, family); } catch { /* ignore */ }
}
export function setCppFormatStyle(style) {
  cppFormatStyle.value = style;
  try { localStorage.setItem(CPP_FORMAT_KEY, style); } catch { /* ignore */ }
}

// Keep in sync with ClangdSession.FormatStyles server-side.
export const CPP_FORMAT_STYLE_OPTIONS = [
  { id: 'LLVM', label: 'LLVM' },
  { id: 'Google', label: 'Google' },
  { id: 'Chromium', label: 'Chromium' },
  { id: 'Mozilla', label: 'Mozilla' },
  { id: 'WebKit', label: 'WebKit' },
  { id: 'Microsoft', label: 'Microsoft' },
  { id: 'GNU', label: 'GNU' },
];

export const THEME_OPTIONS = [
  { id: 'auto', label: 'Follow site theme (default)' },
  { id: 'vs', label: 'Light' },
  { id: 'vs-dark', label: 'Dark' },
  { id: 'bc-dracula', label: 'Dracula' },
  { id: 'bc-monokai', label: 'Monokai' },
  { id: 'bc-solarized-light', label: 'Solarized Light' },
  { id: 'hc-black', label: 'High Contrast Dark' },
  { id: 'hc-light', label: 'High Contrast Light' },
];

export const FONT_OPTIONS = [
  { id: '', label: 'Default' },
  { id: "Consolas, 'Courier New', monospace", label: 'Consolas' },
  { id: 'Menlo, Consolas, monospace', label: 'Menlo' },
  { id: "'Courier New', Courier, monospace", label: 'Courier New' },
  { id: "'Cascadia Code', Consolas, monospace", label: 'Cascadia Code' },
];

/** Resolve the effective Monaco theme id, given the current site light/dark mode. */
export function resolveEditorTheme(siteIsDark) {
  if (editorThemePref.value === 'auto') return siteIsDark ? 'vs-dark' : 'vs';
  return editorThemePref.value;
}

// ---- import a VS Code theme (marketplace themes ship as a JSON file) -----------------
// Best-effort: VS Code themes color TextMate *scopes* (keyword.control.c,
// string.quoted.double, ...) from a full grammar; Monaco's built-in C/C++ tokenizer only
// emits a handful of coarse token names, so only a rough subset of an imported theme's
// rules actually apply. Editor chrome (background, selection, cursor, line numbers — VS
// Code's `colors` map) carries over faithfully since Monaco shares most of the same color
// IDs with VS Code (it's the same editor component).
const CUSTOM_THEME_KEY = 'beecoding.editor.customTheme';        // converted IStandaloneThemeData
const CUSTOM_THEME_NAME_KEY = 'beecoding.editor.customThemeName';
const MAX_IMPORT_BYTES = 2_000_000;

const SCOPE_TO_MONACO = [
  [/comment/i, 'comment'],
  [/string/i, 'string'],
  [/(constant\.numeric|^number)/i, 'number'],
  [/(keyword|storage\.(type|modifier))/i, 'keyword'],
  [/(entity\.name\.type|support\.type|storage\.type)/i, 'type'],
  [/(entity\.name\.function|support\.function)/i, 'function'],
  [/(variable|identifier)/i, 'identifier'],
  [/(keyword\.operator|punctuation)/i, 'delimiter'],
];

function scopesOf(entry) {
  const s = entry.scope;
  if (!s) return [];
  return Array.isArray(s) ? s : String(s).split(',').map((x) => x.trim());
}

/** VS Code theme JSON -> Monaco's IStandaloneThemeData. Exported for testing. */
export function convertVscodeTheme(vt) {
  if (!vt || typeof vt !== 'object') throw new Error('Not a theme file (expected a JSON object).');
  const base = /light/i.test(vt.type || '') ? 'vs' : 'vs-dark';
  const rules = [];
  const seen = new Set();
  for (const entry of Array.isArray(vt.tokenColors) ? vt.tokenColors : []) {
    const fg = entry?.settings?.foreground;
    if (typeof fg !== 'string') continue;
    const hex = fg.replace('#', '');
    const scopes = scopesOf(entry);
    if (scopes.length === 0) {
      if (!seen.has('')) { rules.push({ token: '', foreground: hex }); seen.add(''); }
      continue;
    }
    for (const scope of scopes) {
      for (const [re, token] of SCOPE_TO_MONACO) {
        if (re.test(scope) && !seen.has(token)) {
          rules.push({ token, foreground: hex, fontStyle: entry.settings?.fontStyle || undefined });
          seen.add(token);
        }
      }
    }
  }
  if (rules.length === 0) throw new Error("That theme file doesn't have any usable token colors.");
  return { base, inherit: true, rules, colors: (vt.colors && typeof vt.colors === 'object') ? vt.colors : {} };
}

export function hasCustomTheme() {
  try { return !!localStorage.getItem(CUSTOM_THEME_KEY); } catch { return false; }
}
export function customThemeLabel() {
  try { return localStorage.getItem(CUSTOM_THEME_NAME_KEY) || 'Custom (imported)'; } catch { return 'Custom (imported)'; }
}

/** Fetch (if `input` looks like a URL) or parse `input` as a VS Code theme, register it as
 * the 'bc-custom' Monaco theme, persist it, and switch to it. Throws with a message meant
 * to be shown to the user directly. */
export async function importVscodeTheme(input) {
  const trimmed = (input || '').trim();
  if (!trimmed) throw new Error('Nothing pasted.');
  let text = trimmed;
  if (/^https?:\/\//i.test(trimmed)) {
    let res;
    try { res = await fetch(trimmed); }
    catch { throw new Error("Couldn't reach that URL (network error or the host blocks cross-origin requests)."); }
    if (!res.ok) throw new Error(`That URL returned ${res.status}.`);
    text = await res.text();
  }
  if (text.length > MAX_IMPORT_BYTES) throw new Error('That theme file is too large.');
  let json;
  try { json = JSON.parse(text); }
  catch { throw new Error("That doesn't look like valid JSON. Some VS Code themes use JSON-with-comments (.jsonc) — strip comments and try again."); }

  const converted = convertVscodeTheme(json);
  monaco.editor.defineTheme('bc-custom', converted);
  try {
    localStorage.setItem(CUSTOM_THEME_KEY, JSON.stringify(converted));
    localStorage.setItem(CUSTOM_THEME_NAME_KEY, json.name ? `Custom: ${json.name}` : 'Custom (imported)');
  } catch { /* quota exceeded / private mode — theme still applies this session */ }
  setEditorTheme('bc-custom');
}

function registerCustomThemeFromStorage() {
  try {
    const raw = localStorage.getItem(CUSTOM_THEME_KEY);
    if (raw) monaco.editor.defineTheme('bc-custom', JSON.parse(raw));
  } catch { /* corrupt/old data — ignore */ }
}

// Custom themes are a global Monaco registration, not per-editor — define them once no
// matter how many MonacoEditor instances mount.
let defined = false;
export function defineCustomThemesOnce() {
  if (defined) return;
  defined = true;

  monaco.editor.defineTheme('bc-dracula', {
    base: 'vs-dark', inherit: true,
    rules: [
      { token: '', foreground: 'f8f8f2' },
      { token: 'comment', foreground: '6272a4', fontStyle: 'italic' },
      { token: 'string', foreground: 'f1fa8c' },
      { token: 'number', foreground: 'bd93f9' },
      { token: 'keyword', foreground: 'ff79c6' },
      { token: 'type', foreground: '8be9fd' },
      { token: 'type.identifier', foreground: '8be9fd' },
      { token: 'function', foreground: '50fa7b' },
      { token: 'operator', foreground: 'ff79c6' },
      { token: 'delimiter', foreground: 'f8f8f2' },
    ],
    colors: {
      'editor.background': '#282a36',
      'editor.foreground': '#f8f8f2',
      'editorLineNumber.foreground': '#6272a4',
      'editorCursor.foreground': '#f8f8f2',
      'editor.selectionBackground': '#44475a',
      'editor.lineHighlightBackground': '#2f3140',
    },
  });

  monaco.editor.defineTheme('bc-monokai', {
    base: 'vs-dark', inherit: true,
    rules: [
      { token: '', foreground: 'f8f8f2' },
      { token: 'comment', foreground: '75715e', fontStyle: 'italic' },
      { token: 'string', foreground: 'e6db74' },
      { token: 'number', foreground: 'ae81ff' },
      { token: 'keyword', foreground: 'f92672' },
      { token: 'type', foreground: '66d9ef', fontStyle: 'italic' },
      { token: 'type.identifier', foreground: '66d9ef' },
      { token: 'function', foreground: 'a6e22e' },
      { token: 'operator', foreground: 'f92672' },
      { token: 'delimiter', foreground: 'f8f8f2' },
    ],
    colors: {
      'editor.background': '#272822',
      'editor.foreground': '#f8f8f2',
      'editorLineNumber.foreground': '#75715e',
      'editorCursor.foreground': '#f8f8f2',
      'editor.selectionBackground': '#49483e',
      'editor.lineHighlightBackground': '#3e3d32',
    },
  });

  monaco.editor.defineTheme('bc-solarized-light', {
    base: 'vs', inherit: true,
    rules: [
      { token: '', foreground: '657b83' },
      { token: 'comment', foreground: '93a1a1', fontStyle: 'italic' },
      { token: 'string', foreground: '2aa198' },
      { token: 'number', foreground: 'd33682' },
      { token: 'keyword', foreground: '859900' },
      { token: 'type', foreground: '268bd2' },
      { token: 'type.identifier', foreground: '268bd2' },
      { token: 'function', foreground: 'b58900' },
      { token: 'operator', foreground: '859900' },
      { token: 'delimiter', foreground: '657b83' },
    ],
    colors: {
      'editor.background': '#fdf6e3',
      'editor.foreground': '#657b83',
      'editorLineNumber.foreground': '#93a1a1',
      'editorCursor.foreground': '#657b83',
      'editor.selectionBackground': '#eee8d5',
      'editor.lineHighlightBackground': '#eee8d5',
    },
  });

  registerCustomThemeFromStorage();
}
