<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { langLabel } from '../lib/templates';
import LevelBadge from '../components/LevelBadge.vue';

const router = useRouter();

const items = ref([]);
const q = ref('');
const scope = ref('mine');
const level = ref('');
const error = ref('');

// AI problem generator
const aiEnabled = ref(false);
const genOpen = ref(false);
const genBusy = ref(false);
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
  for (let n = 0; n < 240 && !pollStopped; n++) {   // ~8 min ceiling
    await new Promise((r) => setTimeout(r, 2000));
    if (pollStopped) return;
    let r;
    try {
      r = await api.get(`/api/ai/generate-problem/${jobId}`);
    } catch (e) {
      if (e.status === 404) { finishJob(); genError.value = 'The generation job expired.'; return; }
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
    genError.value = 'Still generating — check the bank in a minute; the draft may appear on its own.';
  }
}

function finishJob() {
  pollStopped = true;
  genBusy.value = false;
  try { localStorage.removeItem(JOB_KEY); } catch { /* ignore */ }
}

async function load() {
  error.value = '';
  try {
    const p = new URLSearchParams({ scope: scope.value });
    if (q.value.trim()) p.set('q', q.value.trim());
    if (level.value) p.set('level', level.value);
    items.value = await api.get(`/api/bank?${p}`);
  } catch (e) { error.value = e.message; }
}
onMounted(async () => {
  load();
  try { aiEnabled.value = (await api.get('/api/ai/enabled'))?.enabled === true; } catch { /* ignore */ }
  // resume a generation that was running when we last left the page
  let pending = null;
  try { pending = localStorage.getItem(JOB_KEY); } catch { /* ignore */ }
  if (pending && aiEnabled.value) { genOpen.value = true; pollJob(pending); }
});
onBeforeUnmount(() => { pollStopped = true; });

</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-8">
    <div class="flex items-center justify-between mb-1 gap-2 flex-wrap">
      <h1 class="text-xl font-bold">Problem bank</h1>
      <div class="flex items-center gap-2">
        <button v-if="aiEnabled" @click="genOpen = !genOpen"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border border-violet-300 dark:border-violet-500/40 text-violet-700 dark:text-violet-300">
          ✨ Generate with AI
        </button>
        <RouterLink to="/bank/new" class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 text-white">
          + New problem
        </RouterLink>
      </div>
    </div>
    <p class="text-sm text-slate-400 dark:text-slate-500 mb-4">
      Your reusable problem library — write a problem once here, then pull it into any
      <RouterLink to="/boards" class="text-amber-600 dark:text-amber-400 hover:underline">board</RouterLink>
      you own. Mark one public and it also joins the pool everyone solves from in
      <RouterLink to="/practice" class="text-amber-600 dark:text-amber-400 hover:underline">Practice</RouterLink>.
    </p>

    <div v-if="genOpen" class="mb-4 border border-violet-200 dark:border-violet-500/30 rounded-xl p-4 space-y-3 bg-violet-50/40 dark:bg-violet-500/5">
      <p class="text-sm font-semibold text-violet-700 dark:text-violet-300">✨ Generate a problem from an idea</p>
      <textarea v-model="gen.idea" rows="3"
                placeholder="e.g. 'count array elements divisible by k', 'check if a graph is bipartite', 'prefix-sum range queries'"
                class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm"></textarea>
      <div class="flex flex-wrap gap-2 text-sm">
        <select v-model="gen.level" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1">
          <option>Easy</option><option>Medium</option><option>Hard</option>
        </select>
        <select v-model="gen.language" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1">
          <option value="cpp">C++</option><option value="c">C</option>
        </select>
        <label class="flex items-center gap-1">tests
          <input v-model.number="gen.count" type="number" min="3" max="15"
                 class="w-14 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1" />
        </label>
        <select v-model="gen.lang" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1">
          <option value="id">Statement: Indonesian</option><option value="en">Statement: English</option>
        </select>
      </div>
      <p class="text-[11px] text-slate-400 dark:text-slate-500">
        The AI writes the statement + a reference solution; the judge runs that solution against the
        inputs, so the stored expected outputs are the real program output. Saved to your bank as a
        private draft — review and publish it yourself. This runs in the background — you can leave
        this page and it will still finish.
      </p>
      <pre v-if="genError" class="text-xs text-red-600 dark:text-red-400 whitespace-pre-wrap max-h-40 overflow-auto">{{ genError }}</pre>
      <div class="flex gap-2 items-center">
        <button @click="generate" :disabled="genBusy || !gen.idea.trim()"
                class="bg-violet-600 hover:bg-violet-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ genBusy ? 'Writing & checking…' : 'Generate' }}
        </button>
        <button v-if="genBusy" @click="finishJob" class="text-slate-500 dark:text-slate-400 text-sm px-3">Stop watching</button>
        <button v-else @click="genOpen = false" class="text-slate-500 dark:text-slate-400 text-sm px-3">Cancel</button>
      </div>
    </div>

    <div class="flex gap-2 mb-4 flex-wrap">
      <input v-model="q" @keyup.enter="load" placeholder="Search title or tag…"
             class="flex-1 min-w-48 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
      <select v-model="scope" @change="load"
              class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
        <option value="mine">Mine</option>
        <option value="public">Shared by others</option>
        <option value="all">All</option>
      </select>
      <select v-model="level" @change="load"
              class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
        <option value="">All levels</option>
        <option>Easy</option><option>Medium</option><option>Hard</option>
      </select>
      <button @click="load" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">Search</button>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ error }}</p>

    <div class="grid sm:grid-cols-2 lg:grid-cols-3 gap-3">
      <div v-for="b in items" :key="b.id"
           class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-3">
        <div class="flex items-start justify-between gap-2">
          <h3 class="font-semibold text-sm">{{ b.title }}</h3>
          <div class="flex items-center gap-1 shrink-0">
            <span v-if="b.generatedByAi" class="text-[10px] px-1.5 py-0.5 rounded-full bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">✨ AI</span>
            <span v-if="b.pendingReview" class="text-[10px] px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300"
                  title="An admin reviews AI-generated problems before they're shared with other teachers">
              ⏳ pending review
            </span>
            <span v-else-if="b.isPublic" class="text-[10px] px-1.5 py-0.5 rounded-full bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
              shared
            </span>
          </div>
        </div>
        <div class="mt-1"><LevelBadge :level="b.level" /></div>
        <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-1">
          {{ langLabel(b.allowedLanguages) }} · {{ b.testCount }} tests ({{ b.sampleCount }} sample)
          <span v-if="!b.mine"> · by {{ b.ownerName }}</span>
        </div>
        <div v-if="b.tags" class="flex flex-wrap gap-1 mt-2">
          <span v-for="t in b.tags.split(',')" :key="t"
                class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
            {{ t }}
          </span>
        </div>
        <RouterLink v-if="b.mine" :to="`/bank/${b.slug}/edit`" class="row-action-btn mt-3 -ml-2">
          <span>✏️</span><span>Edit</span>
        </RouterLink>
      </div>
    </div>
    <p v-if="!items.length" class="text-slate-400 dark:text-slate-500 text-sm">Nothing here yet.</p>

  </div>
</template>
