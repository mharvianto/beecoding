<script>
import { api } from '../lib/api';

// One probe per page load, shared by every row on the page.
let enabledProbe = null;
const aiEnabled = () => (enabledProbe ??= api.get('/api/ai/enabled').then((r) => r?.enabled === true).catch(() => false));
</script>

<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useAuth } from '../stores/auth';

/**
 * Admin-only "AI check": the AI re-solves a problem from its statement alone, the judge runs that solution on
 * every stored test, and any difference from the stored expected output is listed. Advisory — never decides.
 */
const props = defineProps({
  kind: { type: String, required: true },   // 'bank' (a bank problem id) | 'report' (a problem report id)
  id: { type: Number, required: true },
});

const auth = useAuth();
const available = ref(false);
const busy = ref(false);
const error = ref('');
const result = ref(null);
let stopped = false;

onMounted(async () => { if (auth.user?.isAdmin) available.value = await aiEnabled(); });
onBeforeUnmount(() => { stopped = true; });

async function run() {
  error.value = ''; result.value = null; busy.value = true;
  try {
    const { jobId } = await api.post(`/api/ai/check-problem/${props.kind}/${props.id}`);
    for (let n = 0; n < 150 && !stopped; n++) {   // ~5 min ceiling
      await new Promise((r) => setTimeout(r, 2000));
      if (stopped) return;
      let r;
      try { r = await api.get(`/api/ai/check-problem/${jobId}`); }
      catch (e) { if (e.status === 404) { error.value = 'The check expired.'; return; } continue; }
      if (r.status === 'running') continue;
      if (r.status === 'done') result.value = r.problem;
      else error.value = (r.message || 'The check failed.') + (r.compilerOutput ? '\n\n' + r.compilerOutput : '');
      return;
    }
    if (!stopped) error.value = 'Still running — try again in a minute.';
  } catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}

const flagged = (r) => r.tests.filter((t) => t.verdict !== 'match');
</script>

<template>
  <div v-if="available">
    <button @click="run" :disabled="busy" class="row-action-btn row-action-btn--accent disabled:opacity-50">
      <span>🤖</span><span>{{ busy ? 'Checking…' : 'AI check' }}</span>
    </button>
    <pre v-if="error" class="text-xs text-red-600 dark:text-red-400 whitespace-pre-wrap max-h-40 overflow-auto mt-2">{{ error }}</pre>
    <div v-if="result" class="mt-2 border border-violet-200 dark:border-violet-500/30 rounded-lg p-3 bg-violet-50/40 dark:bg-violet-500/5 space-y-2 text-xs">
      <p :class="result.mismatched + result.errored ? 'text-amber-700 dark:text-amber-300' : 'text-emerald-700 dark:text-emerald-300'">
        {{ result.mismatched + result.errored ? '⚠️' : '✅' }} {{ result.summary }}
      </p>
      <div v-for="t in flagged(result)" :key="t.index" class="border border-slate-200 dark:border-slate-800 rounded p-2 bg-white dark:bg-slate-900">
        <div class="text-slate-400 mb-1">{{ t.isSample ? 'sample' : 'hidden' }} #{{ t.index }} · {{ t.verdict }}<template v-if="t.detail"> — {{ t.detail }}</template></div>
        <div v-if="t.verdict === 'mismatch'" class="grid sm:grid-cols-2 gap-2">
          <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto">stored: {{ t.expected }}</pre>
          <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto">AI's output: {{ t.actual }}</pre>
        </div>
      </div>
      <details>
        <summary class="cursor-pointer text-slate-500 dark:text-slate-400">AI's solution ({{ result.language }})</summary>
        <pre class="bg-slate-100 dark:bg-slate-800 rounded p-2 overflow-auto max-h-64 mt-1">{{ result.referenceSolution }}</pre>
      </details>
      <p class="text-[11px] text-slate-400 dark:text-slate-500">
        Advisory only: the AI saw just the statement. A difference means a stored output may be wrong, the statement may be ambiguous, or the AI erred.
      </p>
    </div>
  </div>
</template>
