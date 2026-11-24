<script setup>
import { tr } from '../lib/i18n';
import Icon from '../components/Icon.vue';
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useConfirmDialog } from '../stores/confirmDialog';
import { langLabel } from '../lib/templates';
import LevelBadge from '../components/LevelBadge.vue';
import SimilarProblems from '../components/SimilarProblems.vue';
import { useUrlQuery } from '../lib/urlQuery';

const router = useRouter();
const auth = useAuth();
const confirmDialog = useConfirmDialog();
// A student's bank is a private drafting space: nothing goes live without a reviewer.
const student = computed(() => !auth.isTeacher);

const items = ref([]);
const loaded = ref(false);   // "Nothing here yet." must not flash before the first answer
const q = ref('');
const scope = ref('mine');
const level = ref('');
const error = ref('');

// Search, scope and level live in the URL so a filtered bank survives refresh and can be shared.
const url = useUrlQuery({
  q: { ref: q, def: '' },
  scope: { ref: scope, def: 'mine', allowed: ['mine', 'shared', 'public', 'all'] },
  level: { ref: level, def: '', allowed: ['Easy', 'Medium', 'Hard'] },
}, { onExternalChange: () => load() });
url.read();

// AI problem generator
const aiEnabled = ref(false);
const genOpen = ref(false);
const genBusy = ref(false);
const genElapsed = ref(0);   // seconds since the poll began, so a long wait shows it is still alive
const genError = ref('');
const gen = ref({ idea: '', level: 'Medium', language: 'cpp', count: 10, lang: 'id' });

// Generation runs as a detached server job; we poll it. The job id is kept in
// localStorage so a refresh (or navigating away and back) resumes the poll.
const JOB_KEY = 'beecoding.gen.job';
let pollStopped = false;

async function generate() {
  genError.value = ''; genBusy.value = true;
  try {
    const { jobId } = await api.post('/api/ai/generate-problem', {
      idea: gen.value.idea.trim(),
      level: gen.value.level,
      language: gen.value.language,
      count: Number(gen.value.count),
      lang: gen.value.lang,
    });
    try { localStorage.setItem(JOB_KEY, jobId); } catch { /* ignore */ }
    pollJob(jobId);
  } catch (e) {
    genError.value = e.message;
    genBusy.value = false;
  }
}

async function pollJob(jobId) {
  pollStopped = false;
  genBusy.value = true;
  genElapsed.value = 0;
  for (let n = 0; n < 240 && !pollStopped; n++) {   // ~8 min ceiling
    await new Promise((r) => setTimeout(r, 2000));
    if (pollStopped) return;
    genElapsed.value = (n + 1) * 2;
    let r;
    try {
      r = await api.get(`/api/ai/generate-problem/${jobId}`);
    } catch (e) {
      if (e.status === 404) { finishJob(); genError.value = tr('The generation job expired.'); return; }
      continue;   // transient — keep polling
    }
    if (r.status === 'running') continue;
    finishJob();
    if (r.status === 'done') {
      genOpen.value = false;
      gen.value.idea = '';
      router.push(`/bank/${r.problem.slug}/edit`);
    } else {
      genError.value = (r.message || 'Generation failed.') +
        (r.compilerOutput ? '\n\n' + r.compilerOutput : '') +
        (r.stderr ? '\n\n' + r.stderr : '');
    }
    return;
  }
  if (!pollStopped) {
    finishJob();
    genError.value = tr('Still generating — check the bank in a minute; the draft may appear on its own.');
  }
}

function finishJob() {
  pollStopped = true;
  genBusy.value = false;
  try { localStorage.removeItem(JOB_KEY); } catch { /* ignore */ }
}

async function adminDelete(b) {
  if (!(await confirmDialog.ask(tr('Delete “{p0}” by {p1}? It moves to Trash (Admin → Trash), where you can restore or purge it.', { p0: b.title, p1: b.ownerName }), { confirmLabel: tr('Delete') }))) return;
  error.value = '';
  try { await api.del(`/api/bank/${b.slug}`); await load(); } catch (e) { error.value = e.message; }
}

async function load() {
  error.value = '';
  url.write();
  try {
    const p = new URLSearchParams({ scope: scope.value });
    if (q.value.trim()) p.set('q', q.value.trim());
    if (level.value) p.set('level', level.value);
    items.value = await api.get(`/api/bank?${p}`);
    loadProposals();
  } catch (e) { error.value = e.message; }
  finally { loaded.value = true; }
}
// open "this problem is wrong" reports per bank problem (for the 🚩 badge on the cards)
const openReports = ref({});
async function loadOpenReports() {
  try { openReports.value = (await api.get('/api/problem-reports/open-counts')).bank || {}; } catch { /* no badges */ }
}

// ---- student: send a draft for review ----
const notice = ref('');
const boards = ref([]);
const propose = ref(null);   // { slug, boardSlug, note, busy, error }
const proposalsBySlug = ref({});   // bank slug -> that problem's proposals (student view)

async function loadProposals() {
  if (!student.value) return;
  try {
    const rows = await api.get('/api/me/proposals');
    const by = {};
    for (const r of rows) (by[r.bankSlug] ||= []).push(r);
    proposalsBySlug.value = by;
  } catch { /* statuses just don't show */ }
}

async function withdrawPractice(b) {
  error.value = ''; notice.value = '';
  try { await api.post(`/api/bank/${b.slug}/withdraw-practice`); await load(); }
  catch (e) { error.value = e.message; }
}

async function withdrawProposal(r) {
  error.value = ''; notice.value = '';
  try { await api.post(`/api/me/proposals/${r.id}/withdraw`); await loadProposals(); }
  catch (e) { error.value = e.message; }
}

async function revise(b) {
  error.value = ''; notice.value = '';
  try { const v = await api.post(`/api/bank/${b.slug}/revise`); router.push(`/bank/${v.slug}/edit`); }
  catch (e) { error.value = e.message; }
}

async function submitPractice(b) {
  error.value = ''; notice.value = '';
  if (!(await confirmDialog.ask(tr('Send “{p0}” to an admin for review? Once it is approved it appears in Practice for everyone, and you can no longer edit it.', { p0: b.title }),
    { confirmLabel: tr('Send for review'), danger: false }))) return;
  try {
    await api.post(`/api/bank/${b.slug}/submit-practice`);
    notice.value = tr('Sent for review — you will get a notification when it is answered.');
    await load();
  } catch (e) { error.value = e.message; }
}

async function openPropose(b) {
  propose.value = { slug: b.slug, boardSlug: '', note: '', busy: false, error: '' };
  if (boards.value.length) return;
  try { boards.value = (await api.get('/api/boards')).filter((x) => !x.isOwner && x.allowProposals); } catch { /* the select stays empty */ }
}

async function sendProposal() {
  const pr = propose.value;
  pr.error = ''; pr.busy = true;
  try {
    await api.post(`/api/bank/${pr.slug}/propose/${pr.boardSlug}`, { note: pr.note.trim() || null });
    propose.value = null;
    notice.value = tr('Proposal sent to the board owner — you will get a notification when they answer.');
    loadProposals();
  } catch (e) { pr.error = e.message; pr.busy = false; }
}

onMounted(async () => {
  load();
  loadOpenReports();
  try { aiEnabled.value = (await api.get('/api/ai/enabled'))?.enabled === true; } catch { /* ignore */ }
  // resume a generation that was running when we last left the page
  let pending = null;
  try { pending = localStorage.getItem(JOB_KEY); } catch { /* ignore */ }
  if (pending && aiEnabled.value) { genOpen.value = true; pollJob(pending); }
});
onBeforeUnmount(() => { pollStopped = true; });

</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="flex items-center justify-between mb-1 gap-2 flex-wrap">
      <h1 class="text-xl font-bold">{{ tr('Problem bank') }}</h1>
      <div class="flex items-center gap-2">
        <button v-if="aiEnabled" @click="genOpen = !genOpen"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border border-violet-300 dark:border-violet-500/40 text-violet-700 dark:text-violet-300">
          <Icon name="sparkles" /> {{ tr('Generate with AI') }}
        </button>
        <RouterLink to="/bank/new" class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 text-slate-900">
          {{ tr('+ New problem') }}
        </RouterLink>
      </div>
    </div>
    <p class="text-sm mb-2">
      <RouterLink to="/guide/problems" class="text-amber-700 dark:text-amber-400 hover:underline"><Icon name="book" /> {{ tr('Guide to writing problems') }}</RouterLink>
    </p>
    <p v-if="student" class="text-sm text-slate-500 dark:text-slate-400 mb-4">
      {{ tr('Write your own problems here — they stay private to you. When one is ready, send it to an admin to publish it in') }} <RouterLink to="/practice" class="text-amber-700 dark:text-amber-400 hover:underline">{{ tr('Practice') }}</RouterLink>,
      or propose it to the owner of a board you belong to. A problem can't be edited while it waits for an answer or after it is published. Solving a problem you wrote yourself earns no XP and does not count toward your streak.
    </p>
    <p v-else class="text-sm text-slate-500 dark:text-slate-400 mb-4">
      {{ tr('Your reusable problem library — write a problem once here, then pull it into any') }}
      <RouterLink to="/boards" class="text-amber-700 dark:text-amber-400 hover:underline">{{ tr('board') }}</RouterLink>
      {{ tr('you own. Mark one public and it also joins the pool everyone solves from in') }}
      <RouterLink to="/practice" class="text-amber-700 dark:text-amber-400 hover:underline">{{ tr('Practice') }}</RouterLink>.
    </p>

    <div v-if="genOpen" class="mb-4 border border-violet-200 dark:border-violet-500/30 rounded-xl p-4 space-y-3 bg-violet-50/40 dark:bg-violet-500/5">
      <p class="text-sm font-semibold text-violet-700 dark:text-violet-300"><Icon name="sparkles" /> {{ tr('Generate a problem from an idea') }}</p>
      <textarea v-model="gen.idea" rows="3"
                :placeholder="tr('e.g. \'count array elements divisible by k\', \'check if a graph is bipartite\', \'prefix-sum range queries\'')" :aria-label="tr('e.g. \'count array elements divisible by k\', \'check if a graph is bipartite\', \'prefix-sum range queries\'')"
                class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm"></textarea>
      <div class="flex flex-wrap gap-2 text-sm">
        <select :aria-label="tr('Level')" v-model="gen.level" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1">
          <option>{{ tr('Easy') }}</option><option>{{ tr('Medium') }}</option><option>{{ tr('Hard') }}</option>
        </select>
        <select :aria-label="tr('Language')" v-model="gen.language" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1">
          <option value="cpp">C++</option><option value="c">C</option>
        </select>
        <label class="flex items-center gap-1">{{ tr('tests') }}
          <input v-model.number="gen.count" type="number" min="3" max="15"
                 class="w-14 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1" />
        </label>
        <select :aria-label="tr('Statement language')" v-model="gen.lang" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1">
          <option value="id">{{ tr('Statement: Indonesian') }}</option><option value="en">{{ tr('Statement: English') }}</option>
        </select>
      </div>
      <p class="text-[11px] text-slate-500 dark:text-slate-400">
        The AI writes the statement + a reference solution; the judge runs that solution against the
        inputs, so the stored expected outputs are the real program output. Saved to your bank as a
        private draft — review it, then {{ student ? tr('submit it for review yourself') : tr('publish it yourself') }}. This runs in the background — you can leave
        this page and it will still finish.
      </p>
      <pre v-if="genError" class="text-xs text-red-600 dark:text-red-400 whitespace-pre-wrap max-h-40 overflow-auto">{{ tr(genError) }}</pre>
      <div class="flex gap-2 items-center">
        <button @click="generate" :disabled="genBusy || !gen.idea.trim()"
                class="bg-violet-600 hover:bg-violet-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ genBusy ? `Writing & checking… ${genElapsed}s` : tr('Generate') }}
        </button>
        <button v-if="genBusy" @click="finishJob" class="text-slate-500 dark:text-slate-400 text-sm px-3">{{ tr('Stop watching') }}</button>
        <button v-else @click="genOpen = false" class="text-slate-500 dark:text-slate-400 text-sm px-3">{{ tr('Cancel') }}</button>
      </div>
    </div>

    <div class="flex gap-2 mb-4 flex-wrap">
      <input v-model="q" @keyup.enter="load" :placeholder="tr('Search title or tag…')" :aria-label="tr('Search title or tag…')"
             class="flex-1 min-w-48 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
      <select v-if="!student" v-model="scope" @change="load" :aria-label="tr('Which problems to show')"
              class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 text-sm">
        <option value="mine">{{ tr('Mine') }}</option>
        <option value="shared">{{ tr('Shared with me') }}</option>
        <option value="public">{{ tr('Public from others') }}</option>
        <option value="all">{{ tr('All') }}</option>
      </select>
      <select v-model="level" @change="load"
              class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 text-sm" :aria-label="tr('All levels')">
        <option value="">{{ tr('All levels') }}</option>
        <option>{{ tr('Easy') }}</option><option>{{ tr('Medium') }}</option><option>{{ tr('Hard') }}</option>
      </select>
      <button @click="load" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">{{ tr('Search') }}</button>
    </div>

    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ tr(error) }}</p>
    <p v-if="notice" class="text-sm text-emerald-600 dark:text-emerald-400 mb-3">{{ tr(notice) }}</p>

    <div class="grid sm:grid-cols-2 lg:grid-cols-3 gap-3">
      <div v-for="b in items" :key="b.id"
           class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-3">
        <div class="flex items-start justify-between gap-2">
          <h2 class="font-semibold text-sm">{{ b.title }}</h2>
          <div class="flex items-center gap-1 shrink-0">
            <RouterLink v-if="openReports[b.id]" :to="`/reports/problems?bank=${b.id}`" @click.stop
                        class="text-[11px] px-1.5 py-0.5 rounded-full bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300 hover:underline"
                        :title="`${openReports[b.id]} open report(s) — click to review`"><Icon name="flag" /> {{ openReports[b.id] }}</RouterLink>
            <span v-if="b.generatedByAi" class="text-[11px] px-1.5 py-0.5 rounded-full bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300"><Icon name="sparkles" /> AI</span>
            <span v-if="b.revisionOfId" class="text-[11px] px-1.5 py-0.5 rounded-full bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300"
                  :title="tr('A copy for revising a live problem; approval replaces it')">{{ tr('revision') }}</span>
            <span v-if="b.pendingReview" class="text-[11px] px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300"
                  :title="tr('An admin reviews AI-generated problems before they\'re shared with other teachers')">
              {{ tr('⏳ pending review') }}
            </span>
            <span v-else-if="b.isPublic && student" class="text-[11px] px-1.5 py-0.5 rounded-full bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
              {{ tr('live in Practice') }}
            </span>
            <span v-else-if="b.isPublic" class="text-[11px] px-1.5 py-0.5 rounded-full bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
              {{ tr('shared') }}
            </span>
          </div>
        </div>
        <div class="mt-1"><LevelBadge :level="b.level" /></div>
        <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-1">
          {{ langLabel(b.allowedLanguages) }} · {{ tr('{n} tests ({s} sample)', { n: b.testCount, s: b.sampleCount }) }}
          <span v-if="!b.mine"> · by {{ b.ownerName }}</span>
        </div>
        <div v-if="b.tags" class="flex flex-wrap gap-1 mt-2">
          <span v-for="t in b.tags.split(',')" :key="t"
                class="text-[11px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400">
            {{ t }}
          </span>
        </div>
        <p v-if="b.mine && b.reviewNote" class="text-[11px] text-rose-600 dark:text-rose-400 mt-2">{{ tr('Declined:') }} {{ b.reviewNote }}</p>
        <div v-if="b.mine" class="flex flex-wrap items-center gap-1 mt-3 -ml-2">
          <RouterLink :to="`/practice/${b.slug}`" class="row-action-btn" :title="tr('Solve and submit your own problem to check it works; earns no XP')">
            <span>▶</span><span>{{ tr('Try it') }}</span>
          </RouterLink>
          <RouterLink :to="`/bank/${b.slug}/edit`" class="row-action-btn">
            <span><Icon :name="student && (b.pendingReview || b.isPublic) ? 'eye' : 'pencil'" /></span>
            <span>{{ student && (b.pendingReview || b.isPublic) ? tr('View') : tr('Edit') }}</span>
          </RouterLink>
          <template v-if="student && b.isPublic">
            <RouterLink v-if="b.revisionSlug" :to="`/bank/${b.revisionSlug}/edit`" class="row-action-btn row-action-btn--accent"><span><Icon name="pencil" /></span><span>{{ tr('Continue revision') }}</span></RouterLink>
            <button v-else @click="revise(b)" class="row-action-btn row-action-btn--accent" :title="tr('Edit a private copy; once an admin approves it, it replaces the live problem')"><span><Icon name="pencil" /></span><span>{{ tr('Revise') }}</span></button>
          </template>
          <template v-if="student && !b.isPublic && !b.pendingReview">
            <button @click="submitPractice(b)" class="row-action-btn row-action-btn--accent"><span><Icon name="upload" /></span><span>{{ tr('Submit to Practice') }}</span></button>
            <button @click="openPropose(b)" class="row-action-btn"><span><Icon name="bulb" /></span><span>{{ tr('Propose to a board') }}</span></button>
          </template>
        </div>
        <SimilarProblems v-if="b.mine" :url="`/api/bank/${b.slug}/similar`" class="mt-1 -ml-2" />
        <div v-if="!b.mine && auth.user.isAdmin" class="flex flex-wrap items-center gap-1 mt-3 -ml-2">
          <button @click="adminDelete(b)" class="row-action-btn row-action-btn--danger" :title="tr('Admin: move this problem to Trash')"><span><Icon name="trash" /></span><span>{{ tr('Delete') }}</span></button>
        </div>
        <div v-if="b.mine && b.pendingReview && (student || !b.generatedByAi)" class="mt-1 -ml-2">
          <button @click="withdrawPractice(b)" class="row-action-btn"><span>↩️</span><span>{{ tr('Withdraw from review') }}</span></button>
        </div>
        <ul v-if="proposalsBySlug[b.slug]?.length" class="mt-2 space-y-1">
          <li v-for="r in proposalsBySlug[b.slug]" :key="r.id" class="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1 flex-wrap">
            <span><Icon name="bulb" /> {{ r.boardTitle }}:</span>
            <span :class="{ 'text-amber-700 dark:text-amber-400': r.status === 'Pending', 'text-emerald-600 dark:text-emerald-400': r.status === 'Approved', 'text-rose-600 dark:text-rose-400': r.status === 'Rejected' }">
              {{ { Pending: tr('waiting'), Approved: tr('approved'), Rejected: tr('declined'), Withdrawn: tr('withdrawn') }[r.status] }}
            </span>
            <span v-if="r.reviewNote">— “{{ r.reviewNote }}”</span>
            <button v-if="r.status === 'Pending'" @click="withdrawProposal(r)" class="underline">{{ tr('withdraw') }}</button>
          </li>
        </ul>
        <div v-if="propose && propose.slug === b.slug" class="mt-2 space-y-2 border-t border-slate-200 dark:border-slate-800 pt-2">
          <select :aria-label="tr('Board')" v-model="propose.boardSlug"
                  class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm">
            <option value="" disabled>{{ tr('Choose a board…') }}</option>
            <option v-for="bd in boards" :key="bd.slug" :value="bd.slug">{{ bd.title }}</option>
          </select>
          <input v-model="propose.note" maxlength="300" :placeholder="tr('Message to the board owner (optional)')" :aria-label="tr('Message to the board owner (optional)')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" />
          <p v-if="propose.error" role="alert" class="text-xs text-red-600 dark:text-red-400">{{ propose.error }}</p>
          <div class="flex gap-2">
            <button @click="sendProposal" :disabled="propose.busy || !propose.boardSlug"
                    class="bg-amber-500 text-slate-900 rounded-lg px-3 py-1 text-sm font-medium disabled:opacity-50">{{ tr('Send proposal') }}</button>
            <button @click="propose = null" class="text-slate-500 dark:text-slate-400 text-sm px-2">{{ tr('Cancel') }}</button>
          </div>
        </div>
      </div>
    </div>
    <p v-if="!loaded" role="status" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('Loading…') }}</p>
    <p v-else-if="!items.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('Nothing here yet.') }}</p>

  </div>
</template>
