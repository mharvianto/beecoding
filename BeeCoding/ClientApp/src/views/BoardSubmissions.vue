<script setup>
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { tableView } from '../lib/tableView';
import VerdictBadge from '../components/VerdictBadge.vue';
import SubmissionView from '../components/SubmissionView.vue';
import TableViewToggle from '../components/TableViewToggle.vue';

const props = defineProps({ slug: { type: String, required: true } });
const router = useRouter();

const board = ref(null);
const error = ref('');
const students = ref([]);

// ---- submissions: full history for this board (not just latest-per-student) ----
const submissionRows = ref(null);
const submissionQ = ref('');
const submissionVerdict = ref('');
const submissionUserId = ref('');   // '' = all students
const submissionsPage = ref(1);
const submissionsPageSize = 50;
const submissionsTotal = ref(0);
const viewSubmission = ref(null);   // { id, authorName } | null

async function loadSubmissions() {
  try {
    const p = new URLSearchParams({ page: String(submissionsPage.value), pageSize: String(submissionsPageSize) });
    if (submissionQ.value.trim()) p.set('q', submissionQ.value.trim());
    if (submissionVerdict.value) p.set('verdict', submissionVerdict.value);
    if (submissionUserId.value) p.set('userId', submissionUserId.value);
    const result = await api.get(`/api/boards/${props.slug}/submissions?${p}`);
    submissionRows.value = result.rows;
    submissionsTotal.value = result.total;
  } catch (e) { error.value = e.message; }
}
function searchSubmissions() { submissionsPage.value = 1; loadSubmissions(); }
function submissionsPrevPage() { if (submissionsPage.value > 1) { submissionsPage.value--; loadSubmissions(); } }
function submissionsNextPage() { if (submissionsPage.value * submissionsPageSize < submissionsTotal.value) { submissionsPage.value++; loadSubmissions(); } }
function openSubmission(s) { viewSubmission.value = { id: s.id, authorName: s.userDisplayName }; }

onMounted(async () => {
  try {
    board.value = await api.get(`/api/boards/${props.slug}`);
    if (board.value.role === 'Student') { router.replace(`/boards/${props.slug}`); return; }
    const progress = await api.get(`/api/boards/${props.slug}/progress`);
    students.value = progress.students;
    await loadSubmissions();
  } catch (e) { error.value = e.message; }
});
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-6" v-if="board">
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to {{ board.title }}</RouterLink>
    <div class="flex items-center justify-between mt-2 mb-4">
      <h1 class="text-xl font-bold">Submissions</h1>
      <span class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
        <RouterLink :to="`/boards/${slug}/stats`" class="px-3 py-1.5 text-slate-500 dark:text-slate-400">Statistics</RouterLink>
        <RouterLink :to="`/boards/${slug}/submissions`" class="px-3 py-1.5 bg-slate-800 text-white dark:bg-slate-600">Submissions</RouterLink>
      </span>
    </div>
    <p v-if="error" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ error }}</p>

    <div class="flex flex-wrap gap-2 mb-3">
      <input v-model="submissionQ" @keyup.enter="searchSubmissions" placeholder="Search student or problem…"
             class="flex-1 min-w-0 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
      <select v-model="submissionUserId" @change="searchSubmissions"
              class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
        <option value="">All students</option>
        <option v-for="st in students" :key="st.userId" :value="st.userId">{{ st.displayName }}</option>
      </select>
      <select v-model="submissionVerdict" @change="searchSubmissions"
              class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
        <option value="">Any verdict</option>
        <option value="Accepted">Accepted</option>
        <option value="WrongAnswer">Wrong answer</option>
        <option value="TimeLimit">Time limit</option>
        <option value="MemoryLimit">Memory limit</option>
        <option value="RuntimeError">Runtime error</option>
        <option value="CompileError">Compile error</option>
      </select>
      <button @click="searchSubmissions" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">Search</button>
      <TableViewToggle class="ml-auto" />
    </div>

    <!-- mobile: cards -->
    <div v-if="tableView === 'card'" class="space-y-2">
      <button v-for="s in submissionRows" :key="s.id" @click="openSubmission(s)"
              class="w-full text-left border border-slate-200 dark:border-slate-800 rounded-xl p-3">
        <div class="flex items-center justify-between gap-2 mb-1">
          <VerdictBadge :verdict="s.verdict" small />
          <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ new Date(s.createdAt).toLocaleString() }}</span>
        </div>
        <div class="text-sm font-medium">
          <RouterLink v-if="s.problemSlug" :to="`/boards/${props.slug}/problems/${s.problemSlug}`" @click.stop
                      class="hover:underline hover:text-amber-600 dark:hover:text-amber-400">{{ s.problemTitle }}</RouterLink>
          <template v-else>{{ s.problemTitle }}</template>
        </div>
        <div class="text-[11px] text-slate-400 mt-0.5">
          {{ s.userDisplayName }} · {{ Math.round(s.score * 100) }}% · {{ s.runtimeMs }}ms · {{ s.language || '—' }}
        </div>
      </button>
      <p v-if="submissionRows && !submissionRows.length" class="text-slate-400 dark:text-slate-500 text-sm">No submissions.</p>
    </div>

    <!-- desktop: table -->
    <div v-if="tableView === 'table'" class="overflow-x-auto border border-slate-200 dark:border-slate-800 rounded-xl">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
            <th class="font-normal py-1.5 px-3">When</th><th class="font-normal px-3">Student</th>
            <th class="font-normal px-3">Problem</th><th class="font-normal px-3">Verdict</th>
            <th class="font-normal px-3">Score</th><th class="font-normal px-3">Runtime</th><th class="font-normal px-3">Lang</th>
          </tr>
        </thead>
        <tbody class="[&_td]:py-1.5 [&_td]:px-3">
          <tr v-for="s in submissionRows" :key="s.id" @click="openSubmission(s)"
              class="border-b border-slate-100 dark:border-slate-800/60 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40">
            <td class="text-[11px] text-slate-400 whitespace-nowrap">{{ new Date(s.createdAt).toLocaleString() }}</td>
            <td>{{ s.userDisplayName }}</td>
            <td>
              <RouterLink v-if="s.problemSlug" :to="`/boards/${props.slug}/problems/${s.problemSlug}`" @click.stop
                          class="hover:underline hover:text-amber-600 dark:hover:text-amber-400">{{ s.problemTitle }}</RouterLink>
              <template v-else>{{ s.problemTitle }}</template>
            </td>
            <td><VerdictBadge :verdict="s.verdict" small /></td>
            <td class="tabular-nums">{{ Math.round(s.score * 100) }}%</td>
            <td class="text-[11px] text-slate-400 tabular-nums">{{ s.runtimeMs }}ms</td>
            <td class="text-[11px] text-slate-400">{{ s.language || '—' }}</td>
          </tr>
          <tr v-if="submissionRows && !submissionRows.length"><td colspan="7" class="text-slate-400 dark:text-slate-500 py-3 px-3">No submissions.</td></tr>
        </tbody>
      </table>
    </div>
    <div v-if="submissionsTotal" class="flex items-center gap-3 text-sm mt-3">
      <span class="text-slate-400 dark:text-slate-500">
        {{ (submissionsPage - 1) * submissionsPageSize + 1 }}–{{ Math.min(submissionsPage * submissionsPageSize, submissionsTotal) }} of {{ submissionsTotal }}
      </span>
      <div class="ml-auto flex gap-2">
        <button @click="submissionsPrevPage" :disabled="submissionsPage === 1"
                class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
        <button @click="submissionsNextPage" :disabled="submissionsPage * submissionsPageSize >= submissionsTotal"
                class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
      </div>
    </div>

    <SubmissionView v-if="viewSubmission" :submission-id="viewSubmission.id"
                    :author-name="viewSubmission.authorName" @close="viewSubmission = null" />
  </div>
</template>
