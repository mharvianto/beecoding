import { ref } from 'vue';

// One font-size preference shared by the code editor and the stdin/output panels beside
// it, so the A-/A+ control in the editor resizes all three together. Per-browser, plain
// localStorage (see lib/theme.js for the idiom) — never blocks if storage is unavailable.
const FONT_KEY = 'beecoding.editor.fontSize';
export const MIN_FONT = 9;
export const MAX_FONT = 28;

const coarse = typeof window !== 'undefined' && window.matchMedia?.('(pointer: coarse)').matches;
export const DEFAULT_FONT = coarse ? 14 : 13;

function read() {
  try {
    const n = parseInt(localStorage.getItem(FONT_KEY), 10);
    return Number.isFinite(n) ? Math.min(MAX_FONT, Math.max(MIN_FONT, n)) : DEFAULT_FONT;
  } catch { return DEFAULT_FONT; }
}

export const editorFontSize = ref(read());

export function setEditorFontSize(px) {
  editorFontSize.value = Math.min(MAX_FONT, Math.max(MIN_FONT, Math.round(px)));
  try { localStorage.setItem(FONT_KEY, String(editorFontSize.value)); } catch { /* ignore */ }
}
