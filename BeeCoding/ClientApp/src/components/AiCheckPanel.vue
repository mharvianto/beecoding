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
 * Admin-only AI review aid. kind 'bank' = "AI check": the AI re-solves a problem from its statement alone, the judge
 * runs that solution on every stored test, and any difference from the stored expected output is listed.
 * kind 'report' = "AI triage": the same check on the reported problem plus the AI's read of the report itself.
 * Advisory — never decides.
 */
const props = defineProps({
  kind: { type: String, required: true },   // 'bank' (a bank problem id) | 'report' (a problem report id, triaged)
  id: { type: Number, required: true },
});

const auth = useAuth();
const available = ref(false);
const busy = ref(false);
const error = ref('');
const result = ref(null);
const triage = props.kind === 'report';
let stopped = false;

onMounted(async () => { if (auth.user?.isAdmin) available.value = await aiEnabled(); });
onBeforeUnmount(() => { stopped = true; });

async function run() {
  error.value = ''; result.value = null; busy.value = true;
  try {
    const { jobId } = await api.post(triage ? `/api/ai/triage-report/${props.id}` : `/api/ai/check-problem/bank/${props.id}`);
    for (let n = 0; n < 150 && !stopped; n++) {   // ~5 min ceiling
      await new Promise((r) => setTimeout(r, 2000));
      if (stopped) return;
      let r;
      try { r = await api.get(`/api/ai/${triage ? 'triage-report' : 'check-problem'}/${jobId}`); }
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

const check = (r) => (triage ? r.check : r);   // triage nests the objective check
const flagged = (c) => c.tests.filter((t) => t.verdict !== 'match');

const VERDICT = {
  'likely-valid': ['Report looks valid', 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'],
  'likely-invalid': ['Report looks invalid', 'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-200'],
  unclear: ['Unclear', 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'],
};
const ACTION = {
  'fix-tests': 'Fix the tests', 'fix-statement': 'Clarify the statement', dismiss: 'Dismiss the report', 'ask-reporter': 'Ask the reporter for details',
};
</script>

<template>
  <div v-if="available">
    <button @click="run" :disabled="busy" class="row-action-btn row-action-btn--accent disabled:opacity-50">
      <span>🤖</span><span>{{ busy ? (triage ? 'Triaging…' : 'Checking…') : (triage ? 'AI triage' : 'AI check') }}</span>
    </button>
    <pre v-if="error" class="text-xs text-red-600 dark:text-red-400 whitespace-pre-wrap max-h-40 overflow-auto mt-2">{{ error }}</pre>
    <div v-if="result" class="mt-2 border border-violet-200 dark:border-violet-500/30 rounded-lg p-3 bg-violet-50/40 dark:bg-violet-500/5 space-y-2 text-xs">
      <template v-if="triage">
        <div class="flex items-center gap-2 flex-wrap">
          <span class="px-2 py-0.5 rounded-full font-medium" :class="(VERDICT[result.verdict] || VERDICT.unclear)[1]">{{ (VERDICT[result.verdict] || VERDICT.unclear)[0] }}</span>
          <span class="text-slate-500 dark:text-slate-400">Suggested: {{ ACTION[result.suggestedAction] || ACTION['ask-reporter'] }}</span>
        </div>
        <p class="text-slate-700 dark:text-slate-200 whitespace-pre-wrap">{{ result.reasoning }}</p>
        <p v-if="result.checkNote" class="text-amber-700 dark:text-amber-300">{{ result.checkNote }}</p>
      </template>
      <template v-if="check(result)">
        <p :class="check(result).mismatched + check(result).errored ? 'text-amber-700 dark:text-amber-300' : 'text-emerald-700 dark:text-emerald-300'">
          {{ check(result).mismatched + check(result).errored ? '⚠️' : '✅' }} {{ check(result).summary }}
        </p>
        <div v-for="t in flagged(check(result))" :key="t.index" class="border border-slate-200 dark:border-slate-800 rounded p-2 bg-white dark:bg-slate-900">
          <div class="text-slate-400 mb-1">{{ t.isSample ? 'sample' : 'hidden' }} #{{ t.index }} · {{ t.verdict }}<template v-if="t.detail"> — {{ t.detail }}</template></div>
          <div v-if="t.verdict === 'mismatch'" class="grid sm:grid-cols-2 gap-2">
            <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto">stored: {{ t.expected }}</pre>
            <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto">AI's output: {{ t.actual }}</pre>
          </div>
        </div>
        <details>
          <summary class="cursor-pointer text-slate-500 dark:text-slate-400">AI's solution ({{ check(result).language }})</summary>
          <pre class="bg-slate-100 dark:bg-slate-800 rounded p-2 overflow-auto max-h-64 mt-1">{{ check(result).referenceSolution }}</pre>
        </details>
      </template>
      <p class="text-[11px] text-slate-400 dark:text-slate-500">
        Advisory only: the AI saw just the statement{{ triage ? ' and the report' : '' }}. A difference means a stored output may be wrong, the statement may be ambiguous, or the AI erred.
      </p>
    </div>
  </div>
</template>
