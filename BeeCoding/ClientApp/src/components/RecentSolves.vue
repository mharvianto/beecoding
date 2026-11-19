<script setup>
import { agoShort as ago } from '../lib/timeAgo';
import { tr } from '../lib/i18n';
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


watch(() => [props.organizationId, props.boardId], load);
onMounted(() => {
  load();
  timer = setInterval(() => { if (!document.hidden) load(); }, 30_000);
});
onBeforeUnmount(() => clearInterval(timer));
</script>

<template>
  <div>
    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ tr(error) }}</p>
    <div class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800"
         :class="{ 'opacity-50': loading && !rows.length }">
      <div v-for="r in rows" :key="r.userId + '-' + r.problemSlug"
           class="px-4 py-2.5 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800/50"
           :class="{ 'bg-amber-50 dark:bg-amber-500/10': r.me }">
        <!-- narrow screens: who and when on top, the whole problem title below (it wraps instead of being cut to a few letters) -->
        <div class="sm:hidden">
          <div class="flex items-baseline gap-2">
            <span class="text-sm font-medium truncate min-w-0">{{ r.displayName }}<span v-if="r.me" class="text-xs font-normal text-amber-700 dark:text-amber-400"> {{ tr('(you)') }}</span></span>
            <span class="ml-auto text-[11px] text-slate-500 dark:text-slate-400 shrink-0 tabular-nums">{{ ago(r.solvedAt) }}</span>
          </div>
          <div class="flex items-start gap-2 mt-0.5">
            <RouterLink :to="`/practice/${r.problemSlug}`" class="text-sm hover:underline hover:text-amber-600 dark:hover:text-amber-400 line-clamp-2 min-w-0 flex-1">{{ r.problemTitle }}</RouterLink>
            <LevelBadge :level="r.level" class="shrink-0 mt-0.5" />
          </div>
        </div>
        <!-- wider screens: one row -->
        <div class="hidden sm:flex items-center gap-3">
          <span class="text-sm font-medium truncate w-56 shrink-0">
            {{ r.displayName }}<span v-if="r.me" class="text-xs font-normal text-amber-700 dark:text-amber-400"> {{ tr('(you)') }}</span>
          </span>
          <span class="text-xs text-slate-500 dark:text-slate-400 shrink-0">{{ tr('solved') }}</span>
          <RouterLink :to="`/practice/${r.problemSlug}`" class="text-sm hover:underline hover:text-amber-600 dark:hover:text-amber-400 truncate min-w-0 flex-1">{{ r.problemTitle }}</RouterLink>
          <LevelBadge :level="r.level" />
          <span class="text-[11px] text-slate-500 dark:text-slate-400 w-16 text-right shrink-0 tabular-nums">{{ ago(r.solvedAt) }}</span>
        </div>
      </div>
      <p v-if="!loading && !rows.length" class="px-4 py-6 text-sm text-slate-500 dark:text-slate-400">
        {{ tr('Nothing solved yet.') }} <RouterLink to="/practice" class="text-amber-600">{{ tr('Practice') }}</RouterLink> {{ tr('a problem to be first.') }}
      </p>
    </div>
    <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-2">
      {{ tr('The latest Practice solves (each person\'s first solve of a problem). Problems on a board are not listed here.') }}
    </p>
  </div>
</template>
