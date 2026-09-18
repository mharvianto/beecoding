<script setup>
import { ref, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';

const route = useRoute();
const router = useRouter();

const rows = ref([]);
const error = ref('');
const loading = ref(false);

const orgs = ref([]);
const boards = ref([]);
// 'global' | `org:<slug>` | `board:<slug>` — initialized from the URL so a scoped/windowed
// leaderboard link (e.g. shared in a class group chat) opens straight into that view.
const scope = ref(/^(org|board):[\w-]+$/.test(route.query.scope) ? route.query.scope : 'global');
const period = ref(route.query.period === '1m' ? '1m' : 'all');
const page = ref(1);
const pageSize = 20;
const total = ref(0);

const periods = [['all', 'All time'], ['1m', '1 month']];

const LAST_QUERY_KEY = 'beecoding.leaderboard.lastQuery';
function loadLastQuery() {
  try { return JSON.parse(localStorage.getItem(LAST_QUERY_KEY) || 'null'); }
  catch { return null; }
}
function saveLastQuery() {
  try { localStorage.setItem(LAST_QUERY_KEY, JSON.stringify({ scope: scope.value, period: period.value })); }
  catch { /* quota exceeded / private mode — nothing we can do */ }
}

async function loadOrgs() {
  try { orgs.value = await api.get('/api/me/organizations'); } catch { /* not fatal — just no org tab */ }
}
async function loadBoards() {
  try { boards.value = await api.get('/api/me/boards-brief'); } catch { /* not fatal — just no board picker */ }
}

async function load() {
  loading.value = true; error.value = '';
  try {
    const params = new URLSearchParams({ page: String(page.value), pageSize: String(pageSize), period: period.value });
    if (scope.value.startsWith('org:')) {
      const org = orgs.value.find((o) => o.slug === scope.value.slice(4));
      if (org) params.set('organizationId', org.id);
    } else if (scope.value.startsWith('board:')) {
      const board = boards.value.find((b) => b.slug === scope.value.slice(6));
      if (board) params.set('boardId', board.id);
    }
    const result = await api.get(`/api/leaderboard?${params}`);
    rows.value = result.rows;
    total.value = result.total;
  } catch (e) { error.value = e.message; }
  finally { loading.value = false; }
}
function syncQuery() {
  router.replace({ query: { ...route.query, scope: scope.value, period: period.value } });
  saveLastQuery();
}
function setScope(s) { scope.value = s; page.value = 1; syncQuery(); load(); }
function setBoardScope(e) { setScope(e.target.value ? `board:${e.target.value}` : 'global'); }
function setPeriod(p) { period.value = p; page.value = 1; syncQuery(); load(); }
function prevPage() { if (page.value > 1) { page.value--; load(); } }
function nextPage() { if (page.value * pageSize < total.value) { page.value++; load(); } }

// Browser back/forward (or a direct /leaderboard?scope=...&period=... link) changes the
// query without going through setScope/setPeriod — keep local state in sync.
watch(() => [route.query.scope, route.query.period], ([s, p]) => {
  const nextScope = /^(org|board):[\w-]+$/.test(s) ? s : 'global';
  const nextPeriod = p === '1m' ? '1m' : 'all';
  if (nextScope === scope.value && nextPeriod === period.value) return;
  scope.value = nextScope; period.value = nextPeriod; page.value = 1; load();
});

onMounted(async () => {
  // A bare /leaderboard open (no query at all) restores the last scope/period used — a
  // shared/bookmarked link with its own query always wins and is left untouched.
  if (!route.query.scope && !route.query.period) {
    const saved = loadLastQuery();
    if (saved) {
      scope.value = /^(org|board):[\w-]+$/.test(saved.scope) ? saved.scope : 'global';
      period.value = saved.period === '1m' ? '1m' : 'all';
      router.replace({ query: { ...route.query, scope: scope.value, period: period.value } });
    }
  }
  await Promise.all([loadOrgs(), loadBoards()]);
  await load();
});

const medal = (r) => (r === 1 ? '🥇' : r === 2 ? '🥈' : r === 3 ? '🥉' : '');
const showHowItWorks = ref(false);
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-8">
    <div class="flex items-center gap-2 mb-4">
      <h1 class="text-xl font-bold">Leaderboard</h1>
      <button @click="showHowItWorks = !showHowItWorks"
              class="text-xs text-slate-400 dark:text-slate-500 hover:text-amber-600 dark:hover:text-amber-400 underline decoration-dotted">
        How is ranking calculated?
      </button>
    </div>
    <div v-if="showHowItWorks" class="text-xs text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-800 rounded-lg p-3 mb-4 space-y-1">
      <p>Ranked by total XP earned in the selected scope and period, highest first.</p>
      <p>XP comes from your <em>first</em> Accepted solve of each problem: Easy = 10 XP, Medium = 20 XP, Hard = 40 XP. Solving it again, or an unsuccessful attempt, earns no extra XP.</p>
      <p>Equal XP shares the same rank (e.g. two people tied for #2 are both "#2"; the next distinct XP total is "#4", not "#3").</p>
      <p>On "All time", the ▲/▼ next to your rank shows the change since yesterday — not shown on "1 month" since a rolling window isn't comparable day to day.</p>
    </div>

    <div class="flex flex-col sm:flex-row sm:items-center gap-2 mb-4">
      <div class="flex flex-wrap gap-1.5 text-sm">
        <button @click="setScope('global')" class="rounded-lg px-3 py-1.5"
                :class="scope === 'global' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          Global
        </button>
        <button v-for="o in orgs" :key="o.id" @click="setScope(`org:${o.slug}`)"
                class="rounded-lg px-3 py-1.5 whitespace-nowrap"
                :class="scope === `org:${o.slug}` ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          {{ o.name }}
        </button>
        <select v-if="boards.length" :value="scope.startsWith('board:') ? scope.slice(6) : ''" @change="setBoardScope"
                class="rounded-lg px-2 py-1.5 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 text-sm max-w-[10rem] sm:max-w-none">
          <option value="">This board…</option>
          <option v-for="b in boards" :key="b.id" :value="b.slug">{{ b.title }}</option>
        </select>
      </div>
      <div class="flex gap-1.5 text-sm sm:ml-auto">
        <button v-for="p in periods" :key="p[0]" @click="setPeriod(p[0])"
                class="rounded-lg px-3 py-1.5 whitespace-nowrap"
                :class="period === p[0] ? 'bg-amber-500 text-white' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          {{ p[1] }}
        </button>
      </div>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ error }}</p>

    <div class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800"
         :class="{ 'opacity-50': loading }">
      <div v-for="r in rows" :key="r.userId"
           class="flex items-center gap-3 px-4 py-2.5 bg-white dark:bg-slate-900"
           :class="{ 'bg-amber-50 dark:bg-amber-500/10': r.me }">
        <span class="w-8 text-center text-sm text-slate-400 dark:text-slate-500 shrink-0">{{ medal(r.rank) || r.rank }}</span>
        <span class="w-9 text-xs font-medium tabular-nums shrink-0"
              :class="r.rankDelta > 0 ? 'text-emerald-600 dark:text-emerald-400' : r.rankDelta < 0 ? 'text-rose-600 dark:text-rose-400' : 'text-slate-300 dark:text-slate-600'"
              :title="r.rankDelta ? `${r.rankDelta > 0 ? 'Up' : 'Down'} ${Math.abs(r.rankDelta)} since yesterday` : (r.rankDelta === 0 ? 'Unchanged since yesterday' : '')">
          <template v-if="r.rankDelta > 0">▲{{ r.rankDelta }}</template>
          <template v-else-if="r.rankDelta < 0">▼{{ -r.rankDelta }}</template>
          <template v-else-if="r.rankDelta === 0">–</template>
        </span>
        <div class="flex-1 min-w-0">
          <div class="flex items-center gap-1.5 min-w-0">
            <span class="text-sm font-medium truncate">{{ r.displayName?.trim() || 'Anonymous' }}</span>
            <span v-if="r.me" class="text-xs text-amber-600 dark:text-amber-400 shrink-0">(you)</span>
            <span v-if="r.role === 'Teacher'" class="shrink-0 text-[10px] px-1.5 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">teacher</span>
          </div>
          <div class="text-xs text-slate-400 dark:text-slate-500 mt-0.5 truncate">
            {{ r.solvedCount }} solved · Lv {{ r.level }}
          </div>
        </div>
        <span class="text-sm font-semibold text-amber-600 dark:text-amber-400 shrink-0">{{ r.xp }} XP</span>
      </div>
      <p v-if="!loading && !rows.length" class="px-4 py-6 text-sm text-slate-400 dark:text-slate-500">
        No XP earned{{ period === 'all' ? ' yet' : ' in this period' }}.
        <RouterLink to="/practice" class="text-amber-600">Practice</RouterLink> some problems.
      </p>
    </div>
    <div v-if="total" class="flex items-center gap-3 mt-3 text-sm">
      <span class="text-slate-400 dark:text-slate-500">
        {{ (page - 1) * pageSize + 1 }}–{{ Math.min(page * pageSize, total) }} of {{ total }}
      </span>
      <div class="ml-auto flex gap-2">
        <button @click="prevPage" :disabled="page === 1"
                class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
        <button @click="nextPage" :disabled="page * pageSize >= total"
                class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
      </div>
    </div>
  </div>
</template>
