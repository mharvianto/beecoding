// Editor theme + font family preferences — deliberately NOT toolbar buttons (there are
// already enough of those). Only reachable via Monaco's built-in command palette (F1 /
// Ctrl+Shift+F1), same way the LSP toggle is. Shared across every MonacoEditor instance on
// screen (Vue refs imported from a module are singletons, same pattern as lib/theme.js).
import { ref } from 'vue';
import * as monaco from 'monaco-editor';

const THEME_KEY = 'beecoding.editor.theme';
const FONT_KEY = 'beecoding.editor.fontFamily';

/** 'auto' (follow site light/dark) | a Monaco theme id (built-in or custom-defined below) */
export const editorThemePref = ref(localStorage.getItem(THEME_KEY) || 'auto');
/** '' (Monaco's built-in default stack) | a CSS font-family value */
export const editorFontFamily = ref(localStorage.getItem(FONT_KEY) || '');

export function setEditorTheme(id) {
  editorThemePref.value = id;
  try { localStorage.setItem(THEME_KEY, id); } catch { /* ignore */ }
}
export function setEditorFontFamily(family) {
  editorFontFamily.value = family;
  try { localStorage.setItem(FONT_KEY, family); } catch { /* ignore */ }
}

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
}
