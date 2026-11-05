<script setup>
import { ref, watch } from 'vue';
import { api } from '../lib/api';
import LevelBadge from './LevelBadge.vue';

/** "What next?" after a solve: shown only once the problem is solved, hidden if there is nothing to suggest or the call fails. */
const props = defineProps({ slug: { type: String, required: true }, solved: { type: Boolean, default: false } });
const items = ref([]);

watch(() => [props.slug, props.solved], async ([slug, solved]) => {
  items.value = [];
  if (!solved) return;
  try { items.value = await api.get(`/api/practice/${encodeURIComponent(slug)}/next`); } catch { /* a suggestion is optional */ }
}, { immediate: true });
</script>

<template>
  <div v-if="items.length" class="mb-3 rounded-lg border border-emerald-200 dark:border-emerald-500/30 bg-emerald-50/50 dark:bg-emerald-500/5 px-3 py-2">
    <div class="text-xs font-semibold text-emerald-700 dark:text-emerald-300 mb-1.5">🎯 What next?</div>
    <ul class="space-y-1.5">
      <li v-for="p in items" :key="p.id" class="flex items-center gap-2 flex-wrap text-sm">
        <RouterLink :to="`/practice/${p.slug}`" class="font-medium text-sky-700 dark:text-sky-300 hover:underline">{{ p.title }}</RouterLink>
        <LevelBadge :level="p.level" />
        <span class="text-[11px] text-slate-500 dark:text-slate-400">{{ p.reason }}</span>
      </li>
    </ul>
  </div>
</template>
