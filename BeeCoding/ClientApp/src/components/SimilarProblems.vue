<script setup>
import { ref } from 'vue';
import { api } from '../lib/api';

/**
 * Admin-only review aid: the bank problems closest to this one, to spot a duplicate before approving it. A list for a
 * person to read — the scores are not a verdict (a problem on the same topic scores high too).
 */
const props = defineProps({ id: { type: Number, required: true } });

const busy = ref(false);
const error = ref('');
const result = ref(null);

async function run() {
  error.value = ''; busy.value = true;
  try { result.value = await api.get(`/api/admin-ui/bank/${props.id}/similar`); }
  catch (e) { error.value = e.message; result.value = null; }
  finally { busy.value = false; }
}
</script>

<template>
  <div class="min-w-0">
    <button @click="run" :disabled="busy" class="row-action-btn disabled:opacity-50">
      <span>🔎</span><span>{{ busy ? 'Searching…' : 'Similar problems' }}</span>
    </button>
    <p v-if="error" class="text-xs text-red-600 dark:text-red-400 mt-2">{{ error }}</p>
    <div v-if="result" class="mt-2 border border-slate-200 dark:border-slate-800 rounded-lg p-3 text-xs space-y-1.5">
      <p v-if="!result.items.length" class="text-slate-500 dark:text-slate-400">Nothing to compare with yet.</p>
      <div v-for="s in result.items" :key="s.id" class="flex items-center gap-2">
        <span class="tabular-nums w-10 shrink-0 text-slate-500 dark:text-slate-400">{{ Math.round(s.score * 100) }}%</span>
        <span class="min-w-0 truncate text-slate-700 dark:text-slate-200">{{ s.title }}</span>
        <span class="shrink-0 rounded px-1.5 py-0.5 bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400">{{ s.state }}</span>
      </div>
      <p class="text-[11px] text-slate-400 dark:text-slate-500">
        Compared with {{ result.candidates }} public or pending problems by
        {{ result.method === 'e5' ? 'meaning (e5 embeddings).' : 'shared wording (text overlap) — a rewrite in other words can be missed.' }}
        A high score means look at it, not that it is a duplicate.
      </p>
    </div>
  </div>
</template>
