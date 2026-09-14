<script setup>
import { ref, watch } from 'vue';
import { api } from '../lib/api';
import VerdictBadge from './VerdictBadge.vue';
import MonacoEditor from './MonacoEditor.vue';

const props = defineProps({ submissionId: { type: Number, required: true } });
const emit = defineEmits(['close']);

const sub = ref(null);
const error = ref('');

async function load() {
  error.value = ''; sub.value = null;
  try { sub.value = await api.get(`/api/submissions/${props.submissionId}`); }
  catch (e) { error.value = e.message; }
}
watch(() => props.submissionId, load, { immediate: true });
</script>

<template>
  <div class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50" @click.self="emit('close')">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-3xl my-8 flex flex-col overflow-hidden" style="height: 80vh">
      <div class="flex items-center justify-between px-5 py-3 border-b border-slate-100 dark:border-slate-800 shrink-0">
        <div class="flex items-center gap-2 min-w-0">
          <span v-if="sub" class="font-semibold text-sm truncate">{{ sub.authorName }}</span>
          <VerdictBadge v-if="sub" :verdict="sub.status === 'Done' ? sub.verdict : sub.status" small />
          <span v-if="sub?.status === 'Done'" class="text-xs text-slate-400 dark:text-slate-500">
            {{ sub.runtimeMs }}ms · {{ sub.memoryKb }}KB · {{ Math.round(sub.score * 100) }}%
          </span>
        </div>
        <button @click="emit('close')" class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 shrink-0" title="Close">✕</button>
      </div>

      <p v-if="error" class="text-sm text-red-600 dark:text-red-400 px-5 py-3">{{ error }}</p>
      <div v-else-if="!sub" class="px-5 py-3 text-sm text-slate-400 dark:text-slate-500">Loading…</div>
      <template v-else>
        <div class="flex-1 min-h-0">
          <MonacoEditor :model-value="sub.code || ''" :language="sub.language" :read-only="true"
                         :filename="`${sub.authorName}-submission-${sub.id}`" />
        </div>
        <pre v-if="sub.compilerOutput" class="shrink-0 max-h-32 overflow-auto bg-slate-900 text-slate-100 dark:bg-black text-xs font-mono px-4 py-2 whitespace-pre-wrap">{{ sub.compilerOutput }}</pre>
      </template>
    </div>
  </div>
</template>
