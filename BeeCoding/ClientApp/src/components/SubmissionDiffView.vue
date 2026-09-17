<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import * as monaco from 'monaco-editor';
import { api } from '../lib/api';
import { theme as appTheme } from '../lib/theme';
import { resolveEditorTheme, defineCustomThemesOnce } from '../lib/editorPrefs';

defineCustomThemesOnce();

const props = defineProps({
  submissionAId: { type: Number, required: true },
  submissionBId: { type: Number, required: true },
  source: { type: String, default: 'board' },   // 'board' | 'practice' — both sides always the same source
  // Header labels — default to each submission's own author name when not given.
  labelA: { type: String, default: '' },
  labelB: { type: String, default: '' },
});
const emit = defineEmits(['close']);

const el = ref(null);
const error = ref('');
const loading = ref(true);
const subA = ref(null);
const subB = ref(null);
let diffEditor = null;

function editorTheme() {
  const dark = appTheme.value === 'dark'
    || (appTheme.value === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  return resolveEditorTheme(dark);
}

onMounted(async () => {
  try {
    const base = props.source === 'practice' ? '/api/practice/submissions' : '/api/submissions';
    [subA.value, subB.value] = await Promise.all([
      api.get(`${base}/${props.submissionAId}`),
      api.get(`${base}/${props.submissionBId}`),
    ]);
  } catch (e) { error.value = e.message; }
  finally { loading.value = false; }
  if (error.value) return;

  diffEditor = monaco.editor.createDiffEditor(el.value, {
    theme: editorTheme(),
    readOnly: true,
    automaticLayout: true,
    renderSideBySide: true,
    minimap: { enabled: false },
  });
  const lang = (l) => (l === 'c' ? 'c' : 'cpp');
  diffEditor.setModel({
    original: monaco.editor.createModel(subA.value.code || '', lang(subA.value.language)),
    modified: monaco.editor.createModel(subB.value.code || '', lang(subB.value.language)),
  });
});

onBeforeUnmount(() => {
  const model = diffEditor?.getModel();
  model?.original?.dispose();
  model?.modified?.dispose();
  diffEditor?.dispose();
});
</script>

<template>
  <div class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50" @click.self="emit('close')">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-6xl my-8 flex flex-col overflow-hidden" style="height: 85vh">
      <div class="flex items-center justify-between px-5 py-3 border-b border-slate-100 dark:border-slate-800 shrink-0">
        <div class="flex items-center gap-3 text-sm font-medium min-w-0">
          <span class="truncate">{{ labelA || subA?.authorName }}</span>
          <span class="text-slate-300 dark:text-slate-600">↔</span>
          <span class="truncate">{{ labelB || subB?.authorName }}</span>
        </div>
        <button @click="emit('close')" class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 shrink-0" title="Close">✕</button>
      </div>

      <p v-if="error" class="text-sm text-red-600 dark:text-red-400 px-5 py-3">{{ error }}</p>
      <div v-else-if="loading" class="px-5 py-3 text-sm text-slate-400 dark:text-slate-500">Loading…</div>
      <div v-show="!loading && !error" ref="el" class="flex-1 min-h-0"></div>
    </div>
  </div>
</template>
