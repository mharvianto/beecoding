<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import { useProgress } from '../stores/progress';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';

const progress = useProgress();
const dashboard = ref(null);
const weeklyStats = ref(null);
const topicStats = ref(null);
const err = ref('');

async function load() {
  err.value = '';
  try {
    const [d, weekly, topics] = await Promise.all([
      api.get('/api/me/dashboard'),
      api.get('/api/me/dashboard/weekly?weeks=12'),
      api.get('/api/me/dashboard/topics?take=8'),
    ]);
    dashboard.value = d;
    weeklyStats.value = weekly;
    topicStats.value = topics;
  } catch (e) { err.value = e.message; }
}

const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const fmt = (n) => (n ?? 0).toLocaleString();
const attemptPoints = () => (weeklyStats.value || []).map((w) => ({ label: shortDate(w.weekStart), value: w.attempts }));
const solvedPoints = () => (weeklyStats.value || []).map((w) => ({ label: shortDate(w.weekStart), value: w.solved }));
const topicBarItems = () => (topicStats.value || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));

onMounted(() => { progress.refresh(); load(); });
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-8 space-y-5">
    <div class="flex items-center gap-2">
      <h1 class="text-xl font-bold">Dashboard</h1>
      <button @click="load" class="text-xs text-slate-500 dark:text-slate-400 ml-auto">↻ refresh</button>
    </div>

    <p v-if="err" class="text-sm text-red-600 dark:text-red-400">{{ err }}</p>

    <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-3 gap-3">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <div class="text-xs text-slate-400 dark:text-slate-500">Level</div>
        <div class="text-2xl font-bold">Lv {{ dashboard.level }}</div>
        <div class="mt-1.5 w-full h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
          <span class="block h-full bg-amber-400" :style="{ width: (progress.pct * 100) + '%' }"></span>
        </div>
        <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">{{ fmt(dashboard.xp) }} XP</div>
      </div>

      <RouterLink to="/leaderboard" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
        <div class="text-xs text-slate-400 dark:text-slate-500">Leaderboard rank</div>
        <div class="text-2xl font-bold">{{ dashboard.rank > 0 ? `#${fmt(dashboard.rank)}` : '—' }}</div>
        <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">of {{ fmt(dashboard.rankedUsers) }} ranked</div>
      </RouterLink>

      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <div class="text-xs text-slate-400 dark:text-slate-500">Problems solved</div>
        <div class="text-2xl font-bold">{{ fmt(dashboard.solvedCount) }}</div>
        <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">{{ fmt(dashboard.boardsJoined) }} board(s) joined</div>
      </div>

      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 col-span-2 sm:col-span-1">
        <div class="text-xs text-slate-400 dark:text-slate-500">Submissions</div>
        <div class="text-2xl font-bold">{{ fmt(dashboard.totalAttempts) }}</div>
        <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
          {{ dashboard.totalAttempts ? Math.round(100 * dashboard.acceptedAttempts / dashboard.totalAttempts) : 0 }}% accepted
        </div>
      </div>
    </div>
    <p v-else-if="!err" class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>

    <div v-if="weeklyStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
      <MiniLineChart title="Submissions / week" :points="attemptPoints()" />
      <MiniLineChart title="Problems solved / week" :points="solvedPoints()" />
    </div>

    <div v-if="topicStats?.length">
      <h2 class="font-semibold text-sm mb-1.5">Topics you're struggling with</h2>
      <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">Lowest accept rate first, across boards and practice.</p>
      <TopicBarChart :items="topicBarItems()" />
    </div>
  </div>
</template>
