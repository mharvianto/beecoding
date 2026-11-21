<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';

/** My practice badges: earned ones in colour with the day they were earned, the rest greyed with how far along I am. */
const badges = ref(null);
const error = ref('');
onMounted(async () => {
  try { badges.value = await api.get('/api/me/badges'); } catch (e) { error.value = e.message; }
});
const groups = computed(() => {
  const map = new Map();
  for (const b of badges.value ?? []) { if (!map.has(b.group)) map.set(b.group, []); map.get(b.group).push(b); }
  return [...map.entries()];
});
const earnedCount = computed(() => (badges.value ?? []).filter((b) => b.earned).length);
const day = (iso) => new Date(iso).toLocaleDateString();
</script>

<template>
  <div class="space-y-5">
    <div class="flex items-baseline gap-3 flex-wrap">
      <h2 class="font-semibold text-sm">{{ tr('Badges') }}</h2>
      <span v-if="badges" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('{n} of {total} earned', { n: earnedCount, total: badges.length }) }}</span>
    </div>
    <p class="text-sm text-slate-500 dark:text-slate-400">{{ tr('Earned by solving problems in Practice and on boards. Only you can see them here.') }}</p>
    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(error) }}</p>
    <section v-for="[group, items] in groups" :key="group" class="space-y-2">
      <h3 class="text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr(group) }}</h3>
      <div class="grid gap-2 sm:grid-cols-2">
        <div v-for="b in items" :key="b.key" class="flex items-center gap-3 rounded-xl border px-3 py-2.5"
             :class="b.earned ? 'border-amber-300 dark:border-amber-500/40 bg-amber-50/60 dark:bg-amber-500/5' : 'border-slate-200 dark:border-slate-800'">
          <Icon name="award" class="text-3xl shrink-0" :class="b.earned ? 'text-amber-500' : 'text-slate-300 dark:text-slate-600'" />
          <div class="min-w-0 flex-1">
            <div class="text-sm font-semibold" :class="b.earned ? '' : 'text-slate-500 dark:text-slate-400'">{{ tr(b.name) }}</div>
            <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr(b.description) }}</div>
            <div v-if="b.earned" class="text-[11px] text-amber-700 dark:text-amber-400 mt-0.5">{{ tr('Earned {date}', { date: day(b.earnedAt) }) }}</div>
            <div v-else class="mt-1 flex items-center gap-2">
              <span class="flex-1 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden"><span class="block h-full bg-amber-400" :style="{ width: (b.value / b.target * 100) + '%' }"></span></span>
              <span class="text-[11px] text-slate-500 dark:text-slate-400 tabular-nums">{{ b.value }}/{{ b.target }}</span>
            </div>
          </div>
        </div>
      </div>
    </section>
  </div>
</template>
