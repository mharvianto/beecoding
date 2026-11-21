<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { ref, watch } from 'vue';
import { api } from '../lib/api';
import LevelBadge from './LevelBadge.vue';

/** Shown to a student who has solved everything open on the board: where to go next. Hidden when there is nothing to offer or the call fails. */
const props = defineProps({ boardSlug: { type: String, required: true }, active: { type: Boolean, default: true } });
const items = ref([]);

watch(() => [props.boardSlug, props.active], async ([slug, active]) => {
  items.value = [];
  if (!active) return;
  try {
    const r = await api.get(`/api/practice/after-board/${encodeURIComponent(slug)}`);
    if (slug === props.boardSlug) items.value = r;
  } catch { /* a suggestion is optional */ }
}, { immediate: true });
</script>

<template>
  <div v-if="items.length" class="my-4 rounded-xl border border-emerald-200 dark:border-emerald-500/30 bg-emerald-50/60 dark:bg-emerald-500/5 px-4 py-3">
    <div class="text-sm font-semibold text-emerald-700 dark:text-emerald-300"><Icon name="target" /> {{ tr('You solved everything on this board — keep going in Practice') }}</div>
    <ul class="mt-2 space-y-1.5">
      <li v-for="p in items" :key="p.id" class="flex items-center gap-2 flex-wrap text-sm">
        <RouterLink :to="`/practice/${p.slug}`" class="font-medium hover:underline hover:text-amber-600 dark:hover:text-amber-400">{{ p.title }}</RouterLink>
        <LevelBadge :level="p.level" />
        <span class="text-[11px] text-slate-500 dark:text-slate-400">{{ p.reason }}</span>
      </li>
    </ul>
    <RouterLink to="/practice" class="inline-block mt-2 text-xs text-emerald-700 dark:text-emerald-300 hover:underline">{{ tr('See all of Practice') }} →</RouterLink>
  </div>
</template>
