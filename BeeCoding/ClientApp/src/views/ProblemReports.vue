<script setup>
import { ref, computed, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '../lib/api';
import { useUrlQuery } from '../lib/urlQuery';
import { useProblemReports } from '../stores/problemReports';

// "This problem is wrong" reports for the signed-in teacher's own problems (a platform admin sees all of them).
const route = useRoute();
const queue = useProblemReports();

const status = ref('Open');
const page = ref(1);
const bankProblemId = ref(0);   // narrow to one problem (0 = all): from the 🚩 badges and the bell
const problemId = ref(0);
const data = ref(null);
const error = ref('');
const pageSize = 20;

const url = useUrlQuery({
  status: { ref: status, def: 'Open', allowed: ['Open', 'Resolved', 'Dismissed', 'All'] },
  page: { ref: page, def: 1, int: true },
  bank: { ref: bankProblemId, def: 0, int: true },
  problem: { ref: problemId, def: 0, int: true },
}, { onExternalChange: () => load() });

async function load() {
  error.value = '';
  url.write();
  try {
    const p = new URLSearchParams({ page: String(page.value), pageSize: String(pageSize) });
    if (status.value !== 'All') p.set('status', status.value);
    if (bankProblemId.value) p.set('bankProblemId', String(bankProblemId.value));
    if (problemId.value) p.set('problemId', String(problemId.value));
    data.value = await api.get(`/api/problem-reports?${p}`);
    queue.open = data.value.openTotal;
  } catch (e) { error.value = e.message; }
}
onMounted(() => { url.read(); load(); });

const lastPage = computed(() => Math.max(1, Math.ceil((data.value?.total || 0) / pageSize)));
function setStatus(s) { status.value = s; page.value = 1; load(); }
function go(n) { page.value = Math.min(Math.max(1, n), lastPage.value); load(); }
function clearProblem() { bankProblemId.value = 0; problemId.value = 0; page.value = 1; load(); }

const CATEGORY = {
  WrongInput: ['Input mismatch', 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300'],
  ConstraintViolation: ['Breaks constraints', 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'],
  WrongOutput: ['Wrong output', 'bg-purple-100 text-purple-700 dark:bg-purple-500/15 dark:text-purple-300'],
  StatementUnclear: ['Unclear statement', 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'],
  Other: ['Other', 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300'],
};
const STATUS_STYLE = {
  Open: 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
  Resolved: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  Dismissed: 'bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300',
};
const when = (iso) => new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString();
const link = (r) => (r.kind === 'practice' ? (r.problemSlug && `/practice/${r.problemSlug}`) : (r.problemSlug && `/boards/${r.boardSlug}/problems/${r.problemSlug}`));

// reviewing: one report at a time gets the note box
const reviewing = ref(null);   // { id, status, note }
const busyId = ref(0);
function startReview(r, next) { reviewing.value = { id: r.id, status: next, note: r.note || '' }; }
async function applyReview() {
  const v = reviewing.value;
  busyId.value = v.id; error.value = '';
  try {
    await api.patch(`/api/problem-reports/${v.id}`, { status: v.status, note: v.note });
    reviewing.value = null;
    await load();
  } catch (e) { error.value = e.message; } finally { busyId.value = 0; }
}
const VERB = { Resolved: 'Resolve', Dismissed: 'Dismiss', Open: 'Reopen' };
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-6 sm:py-8">
    <h1 class="text-xl font-bold mb-1">Problem reports</h1>
    <p class="text-sm text-slate-400 dark:text-slate-500 mb-4">
      Reports from students about your problems: test input that doesn't match the statement or breaks its constraints, wrong expected output, unclear text.
    </p>

    <div class="flex items-center gap-1 flex-wrap mb-3">
      <button v-for="s in ['Open', 'Resolved', 'Dismissed', 'All']" :key="s" @click="setStatus(s)"
              class="px-3 py-1 rounded-lg text-sm border"
              :class="status === s ? 'bg-slate-800 text-white border-slate-800 dark:bg-slate-600 dark:border-slate-600' : 'border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800'">
        {{ s }}<span v-if="s === 'Open' && data" class="ml-1 text-xs opacity-80">({{ data.openTotal }})</span>
      </button>
      <button v-if="bankProblemId || problemId" @click="clearProblem" class="ml-2 text-xs text-amber-600 dark:text-amber-400">✕ Showing one problem · show all</button>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ error }}</p>
    <p v-if="data && !data.rows.length" class="text-sm text-slate-400 dark:text-slate-500 border border-dashed border-slate-300 dark:border-slate-700 rounded-xl px-4 py-8 text-center">
      {{ status === 'Open' ? 'No open reports. 🎉' : 'Nothing here.' }}
    </p>

    <div class="space-y-3">
      <article v-for="r in data?.rows" :key="r.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-2 bg-white dark:bg-slate-900">
        <div class="flex items-center gap-2 flex-wrap">
          <RouterLink v-if="link(r)" :to="link(r)" class="font-medium text-sm hover:underline">{{ r.problemTitle }}</RouterLink>
          <span v-else class="font-medium text-sm">{{ r.problemTitle }}</span>
          <span class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ r.kind === 'practice' ? 'Practice' : 'Board' }}</span>
          <span class="text-[11px] px-1.5 py-0.5 rounded-full" :class="CATEGORY[r.category][1]">{{ CATEGORY[r.category][0] }}</span>
          <span class="text-[11px] px-1.5 py-0.5 rounded-full ml-auto" :class="STATUS_STYLE[r.status]">{{ r.status }}</span>
        </div>
        <p class="text-sm whitespace-pre-wrap text-slate-700 dark:text-slate-200">{{ r.message }}</p>
        <div class="text-[11px] text-slate-400 dark:text-slate-500">
          {{ r.reporterName }}<template v-if="r.reporterEmail"> ({{ r.reporterEmail }})</template> · {{ when(r.createdAt) }}
          <template v-if="r.resolvedAt"> · {{ r.status.toLowerCase() }} {{ r.resolvedByName ? `by ${r.resolvedByName}` : '' }} {{ when(r.resolvedAt) }}</template>
        </div>
        <p v-if="r.note && reviewing?.id !== r.id" class="text-xs bg-slate-50 dark:bg-slate-800/60 rounded-lg px-3 py-2 text-slate-600 dark:text-slate-300">📝 {{ r.note }}</p>

        <div v-if="reviewing?.id === r.id" class="space-y-2">
          <textarea v-model="reviewing.note" rows="2" maxlength="1000" placeholder="Optional note: what you changed, or why it's fine as is."
                    class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm"></textarea>
          <div class="flex gap-2">
            <button @click="applyReview" :disabled="busyId === r.id" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-3 py-1.5 text-sm font-medium disabled:opacity-50">{{ VERB[reviewing.status] }}</button>
            <button @click="reviewing = null" class="text-sm text-slate-500 dark:text-slate-400 px-2">Cancel</button>
          </div>
        </div>
        <div v-else class="flex gap-1 flex-wrap">
          <template v-if="r.status === 'Open'">
            <button @click="startReview(r, 'Resolved')" class="row-action-btn row-action-btn--success">✓ Resolve</button>
            <button @click="startReview(r, 'Dismissed')" class="row-action-btn">✕ Dismiss</button>
          </template>
          <button v-else @click="startReview(r, 'Open')" class="row-action-btn">↺ Reopen</button>
        </div>
      </article>
    </div>

    <div v-if="lastPage > 1" class="flex items-center justify-center gap-1 mt-4 text-sm">
      <button @click="go(page - 1)" :disabled="page === 1" class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
      <span class="px-3 text-slate-500 dark:text-slate-400">Page {{ page }} / {{ lastPage }}</span>
      <button @click="go(page + 1)" :disabled="page === lastPage" class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
    </div>
  </div>
</template>
