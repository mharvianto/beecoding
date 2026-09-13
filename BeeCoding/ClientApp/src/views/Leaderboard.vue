<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';

const rows = ref([]);
const error = ref('');
const loading = ref(false);

const orgs = ref([]);
const scope = ref('global');   // 'global' | orgId (number, as string while in the select)
const period = ref('all');     // 'all' | '1y' | '6m' | '1m'

const periods = [['all', 'All time'], ['1y', '1 year'], ['6m', '6 months'], ['1m', '1 month']];

async function loadOrgs() {
  try { orgs.value = await api.get('/api/me/organizations'); } catch { /* not fatal — just no org tab */ }
}

async function load() {
  loading.value = true; error.value = '';
  try {
    const params = new URLSearchParams({ limit: '100', period: period.value });
    if (scope.value !== 'global') params.set('organizationId', scope.value);
    rows.value = await api.get(`/api/leaderboard?${params}`);
  } catch (e) { error.value = e.message; }
  finally { loading.value = false; }
}

onMounted(async () => { await loadOrgs(); await load(); });

const medal = (r) => (r === 1 ? '🥇' : r === 2 ? '🥈' : r === 3 ? '🥉' : '');
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-8">
    <h1 class="text-xl font-bold mb-4">Leaderboard</h1>

    <div class="flex flex-wrap items-center gap-2 mb-4">
      <div class="flex gap-1 text-sm">
        <button @click="scope = 'global'; load()" class="rounded-lg px-3 py-1.5"
                :class="scope === 'global' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          Global
        </button>
        <button v-for="o in orgs" :key="o.id" @click="scope = String(o.id); load()"
                class="rounded-lg px-3 py-1.5 whitespace-nowrap"
                :class="scope === String(o.id) ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          {{ o.name }}
        </button>
      </div>
      <div class="flex gap-1 text-sm sm:ml-auto">
        <button v-for="p in periods" :key="p[0]" @click="period = p[0]; load()"
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
        <span class="w-8 text-center text-sm text-slate-400 dark:text-slate-500">{{ medal(r.rank) || r.rank }}</span>
        <span class="flex-1 text-sm font-medium truncate">
          {{ r.displayName }}
          <span v-if="r.me" class="text-xs text-amber-600 dark:text-amber-400"> (you)</span>
        </span>
        <span v-if="r.role === 'Teacher'" class="text-[10px] px-1.5 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">teacher</span>
        <span class="text-xs font-semibold text-amber-600 dark:text-amber-400">Lv {{ r.level }}</span>
        <span class="text-sm text-slate-500 dark:text-slate-400 w-20 text-right">{{ r.xp }} XP</span>
      </div>
      <p v-if="!loading && !rows.length" class="px-4 py-6 text-sm text-slate-400 dark:text-slate-500">
        No XP earned{{ period === 'all' ? ' yet' : ' in this period' }}.
        <RouterLink to="/practice" class="text-amber-600">Practice</RouterLink> some problems.
      </p>
    </div>
  </div>
</template>
