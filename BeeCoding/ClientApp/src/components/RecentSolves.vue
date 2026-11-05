<script setup>
import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import { api } from '../lib/api';
import LevelBadge from './LevelBadge.vue';

/** The latest 20 Practice solves in the leaderboard's scope. The server only ever sends public problems; refreshed every 30 s while the tab is visible. */
const props = defineProps({ organizationId: Number, boardId: Number });
const rows = ref([]);
const error = ref('');
const loading = ref(false);
let timer = null;
let seq = 0;

async function load() {
  const mine = ++seq;
  loading.value = true; error.value = '';
  try {
    const p = new URLSearchParams();
    if (props.organizationId) p.set('organizationId', props.organizationId);
    if (props.boardId) p.set('boardId', props.boardId);
    const r = await api.get(`/api/leaderboard/recent?${p}`);
    if (mine === seq) rows.value = r;   // a slow answer for another scope must not land on this one
  } catch (e) { if (mine === seq) error.value = e.message; }
  finally { if (mine === seq) loading.value = false; }
}

const now = ref(Date.now());
function ago(iso) {
  const t = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z');
  const s = Math.max(1, Math.floor((now.value - t) / 1000));
  if (s < 60) return 'just now';
  if (s < 3600) return Math.floor(s / 60) + 'm ago';
  if (s < 86400) return Math.floor(s / 3600) + 'h ago';
  return Math.floor(s / 86400) + 'd ago';
}

watch(() => [props.organizationId, props.boardId], load);
onMounted(() => {
  load();
  timer = setInterval(() => { now.value = Date.now(); if (!document.hidden) load(); }, 30_000);
});
onBeforeUnmount(() => clearInterval(timer));
</script>

<template>
  <div>
    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ error }}</p>
    <div class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800"
         :class="{ 'opacity-50': loading && !rows.length }">
      <div v-for="r in rows" :key="r.userId + '-' + r.problemSlug"
           class="flex items-center gap-3 px-4 py-2.5 bg-white dark:bg-slate-900"
           :class="{ 'bg-amber-50 dark:bg-amber-500/10': r.me }">
        <span class="text-sm font-medium truncate min-w-0 sm:w-56 sm:shrink-0">
          {{ r.displayName }}<span v-if="r.me" class="text-xs text-amber-600 dark:text-amber-400"> (you)</span>
        </span>
        <span class="hidden sm:inline text-xs text-slate-400 dark:text-slate-500 shrink-0">solved</span>
        <RouterLink :to="`/practice/${r.problemSlug}`" class="text-sm text-sky-700 dark:text-sky-300 hover:underline truncate min-w-0 flex-1">{{ r.problemTitle }}</RouterLink>
        <LevelBadge :level="r.level" />
        <span class="text-[11px] text-slate-400 dark:text-slate-500 w-16 text-right shrink-0 tabular-nums">{{ ago(r.solvedAt) }}</span>
      </div>
      <p v-if="!loading && !rows.length" class="px-4 py-6 text-sm text-slate-400 dark:text-slate-500">
        Nothing solved yet. <RouterLink to="/practice" class="text-amber-600">Practice</RouterLink> a problem to be first.
      </p>
    </div>
    <p class="text-[11px] text-slate-400 dark:text-slate-500 mt-2">
      The latest Practice solves (each person's first solve of a problem). Problems on a board are not listed here.
    </p>
  </div>
</template>
