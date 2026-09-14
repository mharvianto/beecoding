<script setup>
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useProgress } from '../stores/progress';
import { langLabel } from '../lib/templates';
import LevelBadge from '../components/LevelBadge.vue';
import VerdictBadge from '../components/VerdictBadge.vue';

const route = useRoute();
const router = useRouter();
const progress = useProgress();
const items = ref([]);
// Search text and page live in the URL query (?q=...&page=...) so a search is
// bookmarkable/shareable and survives a refresh or the back button; level/status stay
// local-only (less commonly worth sharing, keeps the URL shorter).
const q = ref(route.query.q ? String(route.query.q) : '');
const level = ref('');
const status = ref('');
const error = ref('');

const page = ref(route.query.page ? Math.max(1, Number(route.query.page) || 1) : 1);
const pageSize = ref(25);
const total = ref(0);
const solvedTotal = ref(0);

const guide = ref({ topics: [], recommended: [], source: 'heuristic', aiAvailable: false });
const showAllTopics = ref(false);
const aiBusy = ref(false);
const aiElapsed = ref(0);
let aiTimer = null;
const shownTopics = computed(() =>
  showAllTopics.value ? guide.value.topics : guide.value.topics.slice(0, 6));

async function loadGuide(useAi = false) {
  if (useAi) {
    aiBusy.value = true;
    aiElapsed.value = 0;
    clearInterval(aiTimer);
    aiTimer = setInterval(() => { aiElapsed.value += 1; }, 1000);
  }
  try {
    guide.value = await api.get('/api/practice/guide' + (useAi ? '?ai=true' : ''));
  } catch { /* non-critical */ }
  finally {
    aiBusy.value = false;
    clearInterval(aiTimer);
    aiTimer = null;
  }
}

function searchTopic(tag) {
  q.value = tag;
  search();
}

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

async function load() {
  error.value = '';
  try {
    const p = new URLSearchParams();
    if (q.value.trim()) p.set('q', q.value.trim());
    if (level.value) p.set('level', level.value);
    if (status.value) p.set('status', status.value);
    p.set('page', page.value);
    p.set('pageSize', pageSize.value);
    const res = await api.get(`/api/practice?${p}`);
    items.value = res.items ?? [];
    total.value = res.total ?? 0;
    solvedTotal.value = res.solved ?? 0;
    page.value = res.page ?? 1;   // server clamps into range
  } catch (e) { error.value = e.message; }
}

function syncUrl() {
  const query = {};
  if (q.value.trim()) query.q = q.value.trim();
  if (page.value > 1) query.page = String(page.value);
  router.replace({ query });
}

// filter change -> back to first page
function search() { page.value = 1; syncUrl(); load(); }
function go(n) {
  const t = Math.min(Math.max(1, n), totalPages.value);
  if (t !== page.value) { page.value = t; syncUrl(); load(); }
}

// Back/forward navigation changes route.query without going through search()/go() —
// the equality check also stops this from re-firing right after our own syncUrl() call.
watch(() => route.query, (query) => {
  const newQ = query.q ? String(query.q) : '';
  const newPage = query.page ? Math.max(1, Number(query.page) || 1) : 1;
  if (newQ === q.value && newPage === page.value) return;
  q.value = newQ;
  page.value = newPage;
  load();
});

onMounted(() => { load(); loadGuide(); progress.refresh(); });
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-6 sm:py-8">
    <div class="flex items-baseline justify-between gap-x-3 gap-y-1 flex-wrap mb-1">
      <h1 class="text-xl font-bold">Practice</h1>
      <div class="text-sm text-slate-500 dark:text-slate-400">
        Lv {{ progress.level }} · {{ progress.xp }} XP · {{ progress.toNext }} XP to next level
      </div>
    </div>
    <p class="text-sm text-slate-400 dark:text-slate-500 mb-4">
      Solve anything from the public problem bank, independent of any class board. Your first
      full solve of a problem earns XP (Easy 10 · Medium 20 · Hard 40).
    </p>

    <!-- what to do next -->
    <div v-if="guide.recommended.length" class="mb-5">
      <div class="flex items-center gap-2 mb-2">
        <h2 class="text-sm font-semibold text-slate-500 dark:text-slate-400">Recommended for you</h2>
        <span v-if="guide.source === 'ai'" class="text-[10px] px-1.5 py-0.5 rounded bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">✨ AI-picked</span>
        <span class="ml-auto"></span>
        <button v-if="guide.aiAvailable && guide.source !== 'ai'" @click="loadGuide(true)" :disabled="aiBusy"
                class="text-xs text-violet-600 dark:text-violet-400 disabled:opacity-50">
          {{ aiBusy ? `Thinking… ${aiElapsed}s` : '✨ Let AI pick' }}
        </button>
        <button v-else-if="guide.source === 'ai'" @click="loadGuide(false)"
                class="text-xs text-slate-400 dark:text-slate-500 hover:text-slate-600">use heuristic</button>
      </div>
      <div class="grid gap-2 sm:grid-cols-3">
        <RouterLink v-for="r in guide.recommended" :key="r.id" :to="`/practice/${r.slug}`"
                    class="border border-amber-200 dark:border-amber-500/30 bg-amber-50/50 dark:bg-amber-500/5 rounded-xl p-3
                           hover:border-amber-400 dark:hover:border-amber-500/60 flex flex-col gap-1">
          <div class="flex items-center gap-2">
            <span class="text-sm font-medium truncate flex-1">{{ r.title }}</span>
            <LevelBadge :level="r.level" />
          </div>
          <span class="text-[11px] text-amber-700 dark:text-amber-400">{{ r.reason }}</span>
          <span v-if="r.tags" class="text-[10px] text-slate-400 dark:text-slate-500 truncate">{{ r.tags }}</span>
        </RouterLink>
      </div>
    </div>

    <!-- topic progress -->
    <div v-if="guide.topics.length" class="mb-5">
      <div class="flex items-center justify-between mb-2">
        <h2 class="text-sm font-semibold text-slate-500 dark:text-slate-400">Your topics</h2>
        <button v-if="guide.topics.length > 6" @click="showAllTopics = !showAllTopics"
                class="text-xs text-amber-600 dark:text-amber-400">
          {{ showAllTopics ? 'Show less' : `Show all (${guide.topics.length})` }}
        </button>
      </div>
      <div class="grid gap-x-6 gap-y-2 sm:grid-cols-2">
        <button v-for="t in shownTopics" :key="t.tag" type="button" @click="searchTopic(t.tag)"
                class="flex items-center gap-2 text-xs text-left hover:text-amber-600 dark:hover:text-amber-400 group"
                :title="`Search problems tagged '${t.tag}'`">
          <span class="w-24 shrink-0 truncate text-slate-500 dark:text-slate-400 group-hover:text-amber-600 dark:group-hover:text-amber-400 group-hover:underline">{{ t.tag }}</span>
          <span class="flex-1 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
            <span class="block h-full bg-amber-400" :style="{ width: (t.total ? t.solved / t.total * 100 : 0) + '%' }"></span>
          </span>
          <span class="w-12 shrink-0 text-right tabular-nums text-slate-400 dark:text-slate-500">{{ t.solved }}/{{ t.total }}</span>
        </button>
      </div>
    </div>

    <div class="flex gap-2 mb-4 flex-wrap">
      <input v-model="q" @keyup.enter="search" placeholder="Search title or tag…"
             class="flex-1 min-w-48 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
      <select v-model="level" @change="search"
              class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
        <option value="">All levels</option>
        <option>Easy</option><option>Medium</option><option>Hard</option>
      </select>
      <select v-model="status" @change="search"
              class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
        <option value="">All statuses</option>
        <option value="unsolved">Unsolved</option>
        <option value="attempted">Attempted</option>
        <option value="solved">Solved</option>
      </select>
      <button @click="search" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">Search</button>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ error }}</p>
    <div class="flex items-center justify-between text-xs text-slate-400 dark:text-slate-500 mb-2">
      <span>{{ total }} problems · {{ solvedTotal }} solved</span>
      <span v-if="total">
        Showing {{ (page - 1) * pageSize + 1 }}–{{ Math.min(page * pageSize, total) }}
      </span>
    </div>

    <div class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800">
      <RouterLink v-for="p in items" :key="p.id" :to="`/practice/${p.slug}`"
                  class="flex items-center gap-3 px-4 py-2.5 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800/60">
        <span class="w-5 text-center">
          <span v-if="p.solved" class="text-emerald-500">✓</span>
          <span v-else-if="p.myVerdict !== 'None'" class="inline-block w-2 h-2 rounded-full bg-amber-400"></span>
        </span>
        <div class="flex-1 min-w-0">
          <span class="text-sm font-medium truncate block">{{ p.title }}</span>
          <span v-if="p.tags" class="md:hidden text-[10px] text-slate-400 dark:text-slate-500 truncate block">{{ p.tags }}</span>
        </div>
        <VerdictBadge v-if="p.myVerdict !== 'None' && !p.solved" :verdict="p.myVerdict" small />
        <span v-if="p.tags" class="hidden md:block text-[11px] text-slate-400 dark:text-slate-500 truncate max-w-[14rem]">{{ p.tags }}</span>
        <LevelBadge :level="p.level" />
        <span class="text-[11px] text-slate-400 dark:text-slate-500 w-10 text-right">{{ langLabel(p.allowedLanguages) }}</span>
      </RouterLink>
      <p v-if="!items.length" class="px-4 py-6 text-sm text-slate-400 dark:text-slate-500">No problems.</p>
    </div>

    <div v-if="totalPages > 1" class="flex items-center justify-center gap-1 mt-4 text-sm">
      <button @click="go(1)" :disabled="page === 1"
              class="px-2 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">«</button>
      <button @click="go(page - 1)" :disabled="page === 1"
              class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
      <span class="px-3 text-slate-500 dark:text-slate-400">Page {{ page }} / {{ totalPages }}</span>
      <button @click="go(page + 1)" :disabled="page === totalPages"
              class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
      <button @click="go(totalPages)" :disabled="page === totalPages"
              class="px-2 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">»</button>
    </div>
  </div>
</template>
