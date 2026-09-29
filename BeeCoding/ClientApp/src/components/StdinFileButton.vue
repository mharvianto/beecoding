<script setup>
import { ref } from 'vue';

// "Load file" button for a stdin box: reads a chosen text file in the browser and emits its
// contents. Nothing is uploaded; the text just replaces the stdin value, so it flows through
// the normal Run request like typed input.
const MAX_BYTES = 1024 * 1024;
const emit = defineEmits(['load']);
const input = ref(null);
const error = ref('');

async function onPick(e) {
  const file = e.target.files?.[0];
  e.target.value = '';   // let the same file be picked again
  if (!file) return;
  if (file.size > MAX_BYTES) { error.value = 'File too large (max 1 MB)'; return; }
  error.value = '';
  try { emit('load', await file.text()); } catch { error.value = 'Could not read file'; }
}
</script>

<template>
  <span class="inline-flex items-center gap-2">
    <button type="button" @click="input.click()"
            class="text-xs text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 underline decoration-dotted">
      Load from file
    </button>
    <span v-if="error" class="text-xs text-red-600 dark:text-red-400">{{ error }}</span>
    <input ref="input" type="file" accept=".txt,.in,.dat,text/plain" class="hidden" @change="onPick" />
  </span>
</template>
