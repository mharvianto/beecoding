<script setup>
import { ref, watch } from 'vue';
import { api } from '../lib/api';
import LevelBadge from './LevelBadge.vue';

/** "Stuck?" card: easier problems on the same topic. The server decides when someone is stuck (it answers with an empty list until then). */
const props = defineProps({ slug: { type: String, required: true }, solved: { type: Boolean, default: false }, attempts: { type: Number, default: 0 } });
const items = ref([]);

watch(() => [props.slug, props.solved, props.attempts], async ([slug, solved]) => {
  if (solved) { items.value = []; return; }
  try {
    const r = await api.get(`/api/practice/${encodeURIComponent(slug)}/easier`);
    if (slug === props.slug) items.value = r;   // the router reuses this component: a slow answer for the previous problem must not land on the next one
  } catch { /* optional */ }
}, { immediate: true });
</script>

<template>
  <div v-if="items.length" class="mb-3 rounded-lg border border-amber-200 dark:border-amber-500/30 bg-amber-50/60 dark:bg-amber-500/5 px-3 py-2">
    <div class="text-xs font-semibold text-amber-700 dark:text-amber-300 mb-1.5">🪜 Stuck? Try an easier one first</div>
    <ul class="space-y-1.5">
      <li v-for="p in items" :key="p.id" class="flex items-center gap-2 flex-wrap text-sm">
        <RouterLink :to="`/practice/${p.slug}`" class="font-medium text-sky-700 dark:text-sky-300 hover:underline">{{ p.title }}</RouterLink>
        <LevelBadge :level="p.level" />
        <span class="text-[11px] text-slate-500 dark:text-slate-400">{{ p.reason }}</span>
      </li>
    </ul>
  </div>
</template>
