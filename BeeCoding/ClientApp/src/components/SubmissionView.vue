<script setup>
import { ref, watch } from 'vue';
import { api } from '../lib/api';
import VerdictBadge from './VerdictBadge.vue';
import MonacoEditor from './MonacoEditor.vue';
import SubmissionDiffView from './SubmissionDiffView.vue';

const props = defineProps({
  submissionId: { type: Number, required: true },
  source: { type: String, default: 'board' },   // 'board' | 'practice'
  // Practice submissions have no author name of their own (practice is solo, not scoped to
  // a board with peers) — pass it in when the caller already knows it (e.g. admin listing).
  authorName: { type: String, default: '' },
});
const emit = defineEmits(['close']);

const sub = ref(null);
const error = ref('');
const showFailedTest = ref(false);
const comparingPrevious = ref(false);

async function load() {
  error.value = ''; sub.value = null; showFailedTest.value = false; comparingPrevious.value = false;
  try {
    sub.value = props.source === 'practice'
      ? await api.get(`/api/practice/submissions/${props.submissionId}`)
      : await api.get(`/api/submissions/${props.submissionId}`);
  }
  catch (e) { error.value = e.message; }
}
watch(() => props.submissionId, load, { immediate: true });
</script>

<template>
  <div class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50" @click.self="emit('close')">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-3xl my-8 flex flex-col overflow-hidden" style="height: 80vh">
      <div class="flex items-center justify-between px-5 py-3 border-b border-slate-100 dark:border-slate-800 shrink-0">
        <div class="flex items-center gap-2 min-w-0">
          <span v-if="sub" class="font-semibold text-sm truncate">{{ authorName || sub.authorName }}</span>
          <VerdictBadge v-if="sub" :verdict="sub.status === 'Done' ? sub.verdict : sub.status" small />
          <span v-if="sub?.status === 'Done'" class="text-xs text-slate-400 dark:text-slate-500">
            {{ sub.runtimeMs }}ms · {{ sub.memoryKb }}KB · {{ Math.round(sub.score * 100) }}%
          </span>
          <button v-if="sub?.previousSubmissionId" @click="comparingPrevious = true" class="row-action-btn shrink-0">
            <span>⇄</span><span>Compare with previous</span>
          </button>
        </div>
        <button @click="emit('close')" class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 shrink-0" title="Close">✕</button>
      </div>

      <p v-if="error" class="text-sm text-red-600 dark:text-red-400 px-5 py-3">{{ error }}</p>
      <div v-else-if="!sub" class="px-5 py-3 text-sm text-slate-400 dark:text-slate-500">Loading…</div>
      <template v-else>
        <div class="flex-1 min-h-0">
          <MonacoEditor :model-value="sub.code || ''" :language="sub.language" :read-only="true"
                         :filename="`${authorName || sub.authorName || 'submission'}-${sub.id}`" />
        </div>
        <pre v-if="sub.compilerOutput" class="shrink-0 max-h-32 overflow-auto bg-slate-900 text-slate-100 dark:bg-black text-xs font-mono px-4 py-2 whitespace-pre-wrap">{{ sub.compilerOutput }}</pre>
        <div v-if="sub.failedTest" class="shrink-0 border-t border-amber-200 dark:border-amber-500/30 bg-amber-50 dark:bg-amber-500/10">
          <button @click="showFailedTest = !showFailedTest"
                  class="w-full flex items-center justify-between px-4 py-2 text-[11px] font-medium text-amber-700 dark:text-amber-300">
            <span>Failed test detail — staff only</span>
            <span>{{ showFailedTest ? '▾ hide' : '▸ show' }}</span>
          </button>
          <pre v-if="showFailedTest" class="max-h-48 overflow-auto text-xs font-mono text-slate-700 dark:text-slate-200 whitespace-pre-wrap px-4 pb-2">{{ sub.failedTest }}</pre>
        </div>
      </template>
    </div>

    <SubmissionDiffView v-if="comparingPrevious" :submission-a-id="sub.previousSubmissionId" :submission-b-id="sub.id"
                        :source="source" label-a="Previous attempt" label-b="This attempt" @close="comparingPrevious = false" />
  </div>
</template>
