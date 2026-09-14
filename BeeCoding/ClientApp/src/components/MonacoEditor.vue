<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue';
import * as monaco from 'monaco-editor';
import { theme as appTheme } from '../lib/theme';
import { CppLsp } from '../lib/cpplsp';
import { withBase } from '../lib/base';
import {
  editorThemePref, editorFontFamily, setEditorTheme, setEditorFontFamily,
  THEME_OPTIONS, FONT_OPTIONS, resolveEditorTheme, defineCustomThemesOnce,
  importVscodeTheme, hasCustomTheme, customThemeLabel,
} from '../lib/editorPrefs';

defineCustomThemesOnce();

function editorTheme() {
  const dark = appTheme.value === 'dark'
    || (appTheme.value === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  return resolveEditorTheme(dark);
}

const props = defineProps({
  modelValue: { type: String, default: '' },
  language: { type: String, default: 'cpp' },
  // when set to 'c' | 'cpp', connect the clangd LSP bridge for this editor
  lsp: { type: [String, Boolean], default: false },
  readOnly: { type: Boolean, default: false },
  // base filename (no extension) offered when downloading this editor's content
  filename: { type: String, default: 'code' },
});
const emit = defineEmits(['update:modelValue']);

const el = ref(null);
let editor = null;
let selfEmit = false;   // true while we're emitting our own change — don't echo it back

// Touch devices: Monaco's virtual-keyboard/IME handling is fragile. The browser
// EditContext API fixes most of the "typed char lands wrong / deletes the wrong
// one" desync; auto-closing pairs and Enter-accepts-suggestion make it worse on a
// phone, so turn those off there.
const coarse = typeof window !== 'undefined' && window.matchMedia?.('(pointer: coarse)').matches;
const hasEditContext = typeof window !== 'undefined' && 'EditContext' in window;

// ---- per-browser editor prefs (font size, LSP on/off) -----------------------
const FONT_KEY = 'beecoding.editor.fontSize';
const LSP_KEY = 'beecoding.editor.lsp';
const DEFAULT_FONT = coarse ? 14 : 13;

function readFont() {
  try {
    const n = parseInt(localStorage.getItem(FONT_KEY), 10);
    return Number.isFinite(n) ? Math.min(28, Math.max(9, n)) : DEFAULT_FONT;
  } catch { return DEFAULT_FONT; }
}
const fontSize = ref(readFont());
const lspEnabled = ref((() => { try { return localStorage.getItem(LSP_KEY) !== '0'; } catch { return true; } })());

// Is the clangd bridge switched on server-side? Asked once, shared by every editor.
// Without this the editor opens a WebSocket that the server rejects -> console error.
const serverLsp = ref(false);
let serverLspProbe = null;
function probeServerLsp() {
  serverLspProbe ??= fetch(withBase('/api/lsp/enabled'), { credentials: 'include' })
    .then((r) => (r.ok ? r.json() : null))
    .then((j) => j?.enabled === true)
    .catch(() => false);
  return serverLspProbe;
}

// this editor can talk to clangd at all?
const lspCapable = () => props.lsp === true || props.lsp === 'c' || props.lsp === 'cpp';

function applyFont(px) {
  fontSize.value = Math.min(28, Math.max(9, Math.round(px)));
  editor?.updateOptions({ fontSize: fontSize.value });
  try { localStorage.setItem(FONT_KEY, String(fontSize.value)); } catch { /* ignore */ }
}
function toggleLsp() {
  lspEnabled.value = !lspEnabled.value;
  try { localStorage.setItem(LSP_KEY, lspEnabled.value ? '1' : '0'); } catch { /* ignore */ }
  if (lspEnabled.value) initLsp(); else disposeLsp();
}

// ---- clangd LSP wiring --------------------------------------------------------
let lspClient = null;
let lspDisposables = [];
let changeTimer = null;

const COMPLETION_KIND = {           // LSP CompletionItemKind -> monaco
  1: 18, 2: 1, 3: 1, 4: 0, 5: 3, 6: 4, 7: 6, 8: 7, 9: 8, 10: 9, 11: 12, 12: 13,
  13: 15, 14: 17, 15: 27, 16: 19, 17: 20, 18: 18, 19: 23, 20: 16, 21: 14, 22: 5,
  23: 22, 24: 10, 25: 24,
};
const MARKER_SEV = { 1: 8, 2: 4, 3: 2, 4: 1 };   // LSP DiagnosticSeverity -> monaco.MarkerSeverity

const toLspPos = (p) => ({ line: p.lineNumber - 1, character: p.column - 1 });
const asText = (c) => (typeof c === 'string' ? c : Array.isArray(c) ? c.map(asText).join('\n\n') : (c?.value ?? ''));

function toMonacoCompletion(it, fallbackRange) {
  const edit = it.textEdit;
  let range = fallbackRange;
  const e = edit?.range || edit?.replace;
  if (e) {
    range = new monaco.Range(e.start.line + 1, e.start.character + 1, e.end.line + 1, e.end.character + 1);
  }
  return {
    label: (it.label || '').replace(/^\s+/, ''),   // clangd left-pads labels
    kind: COMPLETION_KIND[it.kind] ?? 0,
    detail: it.detail || (it.labelDetails?.detail ?? ''),
    documentation: it.documentation ? { value: asText(it.documentation) } : undefined,
    insertText: edit?.newText ?? it.insertText ?? it.label,
    insertTextRules: it.insertTextFormat === 2
      ? monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet : 0,
    range,
    sortText: it.sortText,
    filterText: it.filterText,
    commitCharacters: it.commitCharacters,
  };
}

async function initLsp() {
  if (!lspEnabled.value || props.readOnly || !serverLsp.value) return;
  const lang = props.lsp === true ? props.language : props.lsp;
  if (lang !== 'c' && lang !== 'cpp') return;

  try {
    lspClient = new CppLsp(lang);
    await lspClient.connect();
    await lspClient.open(editor.getValue());
  } catch {
    lspClient = null;               // bridge disabled / clangd missing -> word-based only
    return;
  }

  lspDisposables.push(editor.onDidChangeModelContent(() => {
    clearTimeout(changeTimer);
    changeTimer = setTimeout(() => lspClient?.didChange(editor.getValue()), 250);
  }));

  const mine = () => lspClient && editor && editor.getModel();

  lspDisposables.push(monaco.languages.registerCompletionItemProvider(props.language, {
    triggerCharacters: ['.', '>', ':', '<', '"', '/', ' '],
    async provideCompletionItems(model, position, ctx) {
      if (!mine() || model !== editor.getModel()) return { suggestions: [] };
      const r = await lspClient.completion(toLspPos(position), ctx?.triggerCharacter);
      const items = r?.items ?? (Array.isArray(r) ? r : []);
      const w = model.getWordUntilPosition(position);
      const range = new monaco.Range(position.lineNumber, w.startColumn, position.lineNumber, w.endColumn);
      return { suggestions: items.map((it) => toMonacoCompletion(it, range)), incomplete: !!r?.isIncomplete };
    },
  }));

  lspDisposables.push(monaco.languages.registerHoverProvider(props.language, {
    async provideHover(model, position) {
      if (!mine() || model !== editor.getModel()) return null;
      const h = await lspClient.hover(toLspPos(position));
      if (!h?.contents) return null;
      return { contents: [{ value: asText(h.contents) }] };
    },
  }));

  lspDisposables.push(monaco.languages.registerSignatureHelpProvider(props.language, {
    signatureHelpTriggerCharacters: ['(', ','],
    signatureHelpRetriggerCharacters: [','],
    async provideSignatureHelp(model, position) {
      if (!mine() || model !== editor.getModel()) return null;
      const s = await lspClient.signatureHelp(toLspPos(position));
      if (!s?.signatures?.length) return null;
      return {
        value: {
          signatures: s.signatures.map((sig) => ({
            label: sig.label,
            documentation: sig.documentation ? { value: asText(sig.documentation) } : undefined,
            parameters: (sig.parameters || []).map((p) => ({ label: p.label })),
          })),
          activeSignature: s.activeSignature ?? 0,
          activeParameter: s.activeParameter ?? 0,
        },
        dispose() {},
      };
    },
  }));

  lspClient.onDiagnostics((diags) => {
    const model = editor?.getModel();
    if (!model) return;
    monaco.editor.setModelMarkers(model, 'clangd', (diags || []).map((d) => ({
      severity: MARKER_SEV[d.severity] ?? 4,
      message: d.message,
      source: d.source || 'clangd',
      startLineNumber: d.range.start.line + 1,
      startColumn: d.range.start.character + 1,
      endLineNumber: d.range.end.line + 1,
      endColumn: d.range.end.character + 1,
    })));
  });
}

function disposeLsp() {
  clearTimeout(changeTimer);
  lspDisposables.forEach((d) => { try { d.dispose(); } catch { /* ignore */ } });
  lspDisposables = [];
  try { if (editor?.getModel()) monaco.editor.setModelMarkers(editor.getModel(), 'clangd', []); } catch { /* ignore */ }
  lspClient?.close();
  lspClient = null;
}

// ---- editor lifecycle -------------------------------------------------------
onMounted(() => {
  editor = monaco.editor.create(el.value, {
    value: props.modelValue,
    language: props.language,
    theme: editorTheme(),
    fontSize: fontSize.value,
    fontFamily: editorFontFamily.value || undefined,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    automaticLayout: true,
    tabSize: 4,
    readOnly: props.readOnly,
    experimentalEditContext: hasEditContext,
    // on touch devices the font-size/LSP overlay is always visible (no hover to
    // reveal it), so give line 1 room to clear it instead of sitting underneath.
    padding: coarse ? { top: 26 } : undefined,
    ...(coarse ? {
      wordWrap: 'on',
      autoClosingBrackets: 'never',
      autoClosingQuotes: 'never',
      autoClosingOvertype: 'never',
      autoSurround: 'never',
      acceptSuggestionOnEnter: 'off',
      tabCompletion: 'off',
      contextmenu: false,
    } : {}),
  });
  editor.onDidChangeModelContent(() => {
    selfEmit = true;
    emit('update:modelValue', editor.getValue());
    selfEmit = false;
  });

  // right-click menu / shortcuts: font size + LSP toggle
  editor.addAction({
    id: 'beecoding.fontInc', label: 'Editor: Increase font size',
    keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Equal],
    contextMenuGroupId: 'zz_beecoding', contextMenuOrder: 1,
    run: () => applyFont(fontSize.value + 1),
  });
  editor.addAction({
    id: 'beecoding.fontDec', label: 'Editor: Decrease font size',
    keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Minus],
    contextMenuGroupId: 'zz_beecoding', contextMenuOrder: 2,
    run: () => applyFont(fontSize.value - 1),
  });
  editor.addAction({
    id: 'beecoding.fontReset', label: 'Editor: Reset font size',
    keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Digit0],
    contextMenuGroupId: 'zz_beecoding', contextMenuOrder: 3,
    run: () => applyFont(DEFAULT_FONT),
  });
  // Theme + font family: command palette only (F1) — deliberately not toolbar buttons,
  // there are enough of those already. Each choice is its own palette entry since the
  // standalone monaco-editor package has no quick-pick API to show a sub-menu from a
  // single action.
  THEME_OPTIONS.forEach((t) => editor.addAction({
    id: `beecoding.theme.${t.id}`, label: `Editor: Theme — ${t.label}`,
    run: () => setEditorTheme(t.id),
  }));
  FONT_OPTIONS.forEach((f) => editor.addAction({
    id: `beecoding.font.${f.label}`, label: `Editor: Font — ${f.label}`,
    run: () => setEditorFontFamily(f.id),
  }));
  editor.addAction({
    id: 'beecoding.theme.import', label: 'Editor: Theme — Import from URL or JSON…',
    run: async () => {
      const input = window.prompt(
        'Paste a VS Code theme\'s raw JSON URL, or paste the theme JSON itself.\n' +
        'Fidelity is best-effort — editor colors carry over well, syntax highlighting is approximate.');
      if (!input) return;
      try { await importVscodeTheme(input); }
      catch (e) { window.alert(`Couldn't import that theme: ${e.message}`); }
    },
  });
  if (hasCustomTheme()) {
    editor.addAction({
      id: 'beecoding.theme.custom', label: `Editor: Theme — ${customThemeLabel()}`,
      run: () => setEditorTheme('bc-custom'),
    });
  }
  if (lspCapable() && !props.readOnly) {
    probeServerLsp().then((ok) => {
      serverLsp.value = ok;
      if (!ok) return;
      editor?.addAction({
        id: 'beecoding.toggleLsp', label: 'Editor: Toggle C/C++ IntelliSense (LSP)',
        contextMenuGroupId: 'zz_beecoding', contextMenuOrder: 4,
        run: () => toggleLsp(),
      });
      if (!props.readOnly) initLsp();
    });
  }
});

watch(() => props.readOnly, (ro) => editor?.updateOptions({ readOnly: ro }));

watch(() => props.modelValue, (v) => {
  if (!editor || selfEmit) return;
  if (v === editor.getValue()) return;
  // A read-only editor (live viewer) must always follow modelValue. executeEdits is a
  // no-op while readOnly is on, so replace the whole buffer with setValue instead.
  if (props.readOnly) { editor.setValue(v || ''); return; }
  // Editable: don't yank text out from under someone who is typing (scrambled input on
  // mobile); apply as a scoped edit so the undo stack survives.
  if (editor.hasTextFocus()) return;
  const model = editor.getModel();
  editor.executeEdits('external', [{ range: model.getFullModelRange(), text: v || '' }]);
  editor.pushUndoStop();
});
watch(() => props.language, (l) => {
  if (editor) monaco.editor.setModelLanguage(editor.getModel(), l);
  disposeLsp();
  initLsp();
});
watch(appTheme, () => monaco.editor.setTheme(editorTheme()));
watch(editorThemePref, () => monaco.editor.setTheme(editorTheme()));
watch(editorFontFamily, (f) => editor?.updateOptions({ fontFamily: f || undefined }));

onBeforeUnmount(() => { disposeLsp(); editor?.dispose(); });

const showLspBtn = lspCapable() && !props.readOnly;
const btnCls =
  'px-1.5 py-0.5 rounded bg-white/85 dark:bg-slate-800/85 border border-slate-300 dark:border-slate-600 ' +
  'text-slate-600 dark:text-slate-300 hover:bg-white dark:hover:bg-slate-700 shadow-sm leading-none';

function download() {
  const ext = props.language === 'c' ? 'c' : props.language === 'cpp' ? 'cpp' : 'txt';
  const blob = new Blob([editor?.getValue() ?? props.modelValue], { type: 'text/plain' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${props.filename}.${ext}`;
  a.click();
  URL.revokeObjectURL(url);
}
</script>

<template>
  <div class="relative h-full w-full">
    <div ref="el" class="h-full w-full"></div>
    <div class="absolute top-1 right-4 z-10 flex items-center gap-1 text-[11px] font-mono transition-opacity
                opacity-30 hover:opacity-100 focus-within:opacity-100"
         :class="{ '!opacity-90': coarse }">
      <button type="button" :class="btnCls" title="Decrease font size (Ctrl -)" @click="applyFont(fontSize - 1)">A&minus;</button>
      <span class="text-slate-400 dark:text-slate-500 tabular-nums w-4 text-center">{{ fontSize }}</span>
      <button type="button" :class="btnCls" title="Increase font size (Ctrl +)" @click="applyFont(fontSize + 1)">A+</button>
      <button v-if="showLspBtn && serverLsp" type="button" :class="btnCls"
              :title="lspEnabled ? 'C/C++ IntelliSense on — click to disable' : 'C/C++ IntelliSense off — click to enable'"
              @click="toggleLsp()">
        LSP&nbsp;{{ lspEnabled ? 'on' : 'off' }}
      </button>
      <button type="button" :class="btnCls" title="Download as a file" @click="download()">&#8681;</button>
    </div>
  </div>
</template>
