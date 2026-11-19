<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue';
import * as monaco from 'monaco-editor';
import { theme as appTheme } from '../lib/theme';
import { editorFontSize } from '../lib/editorFont';
import { editorFontFamily, resolveEditorTheme, defineCustomThemesOnce } from '../lib/editorPrefs';

// Read-only Monaco diff: `original` on the left, `modified` on the right (one column when the space is narrow).
const props = defineProps({
  original: { type: String, default: '' },
  modified: { type: String, default: '' },
  language: { type: String, default: 'cpp' },
});

defineCustomThemesOnce();
const dark = () => appTheme.value === 'dark' || (appTheme.value === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);

const el = ref(null);
let diff = null, a = null, b = null;

onMounted(() => {
  diff = monaco.editor.createDiffEditor(el.value, {
    theme: resolveEditorTheme(dark()),
    readOnly: true,
    originalEditable: false,
    automaticLayout: true,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    renderSideBySide: true,
    ignoreTrimWhitespace: false,   // a change in trailing spaces is still a change worth seeing
    fontSize: editorFontSize.value,
    fontFamily: editorFontFamily.value || undefined,
  });
  a = monaco.editor.createModel(props.original, props.language);
  b = monaco.editor.createModel(props.modified, props.language);
  diff.setModel({ original: a, modified: b });
});
watch(() => props.original, (v) => a?.setValue(v));
watch(() => props.modified, (v) => b?.setValue(v));
watch(appTheme, () => monaco.editor.setTheme(resolveEditorTheme(dark())));
onBeforeUnmount(() => { diff?.dispose(); a?.dispose(); b?.dispose(); });
</script>

<template>
  <div ref="el" class="h-full w-full rounded overflow-hidden border border-slate-200 dark:border-slate-800"></div>
</template>
