<script setup>
import { vModal } from '../lib/vModal';
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import { langLabel } from '../lib/templates';
import LevelBadge from './LevelBadge.vue';

const props = defineProps({ boardSlug: { type: String, required: true } });
const emit = defineEmits(['added', 'cancel']);

const items = ref([]);
const q = ref('');
const scope = ref('all');
const error = ref('');
const busyId = ref(null);

async function load() {
  error.value = '';
  try {
    const p = new URLSearchParams({ scope: scope.value });
    if (q.value.trim()) p.set('q', q.value.trim());
    items.value = await api.get(`/api/bank?${p}`);
  } catch (e) { error.value = e.message; }
}
onMounted(load);

async function add(item) {
  busyId.value = item.id;
  try {
    await api.post(`/api/bank/${item.id}/copy-to/${props.boardSlug}`);
    emit('added');
  } catch (e) { error.value = e.message; }
  finally { busyId.value = null; }
}
</script>

<template>
  <div role="dialog" aria-modal="true" v-modal="() => emit('cancel')" :aria-label="tr('Pick a problem from the bank')" class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-2xl p-4 sm:p-5 my-4 sm:my-8">
      <div class="flex items-center justify-between mb-3">
        <h2 class="font-bold text-lg">{{ tr('Add from problem bank') }}</h2>
        <button @click="emit('cancel')" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" :aria-label="tr('Close')">✕</button>
      </div>

      <div class="flex flex-wrap gap-2 mb-3">
        <input v-model="q" @keyup.enter="load" :placeholder="tr('Search title or tag…')" :aria-label="tr('Search title or tag…')"
               class="flex-1 min-w-0 basis-full sm:basis-0 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
        <select v-model="scope" @change="load"
                class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-sm flex-1 sm:flex-none">
          <option value="all">{{ tr('All') }}</option>
          <option value="mine">{{ tr('Mine') }}</option>
          <option value="public">{{ tr('Shared') }}</option>
        </select>
        <button @click="load" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5">{{ tr('Search') }}</button>
      </div>

      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400 mb-2">{{ tr(error) }}</p>

      <div class="space-y-2 max-h-[60vh] overflow-y-auto">
        <div v-for="b in items" :key="b.id"
             class="border border-slate-200 dark:border-slate-800 rounded-lg px-3 py-2 flex items-center gap-3">
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-x-1.5 gap-y-1">
              <span class="font-medium text-sm break-words">{{ b.title }}</span>
              <LevelBadge :level="b.level" class="shrink-0" />
              <span v-if="b.generatedByAi" class="shrink-0 whitespace-nowrap text-[11px] px-1.5 py-0.5 rounded-full bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300"><Icon name="sparkles" /> AI</span>
            </div>
            <div class="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              {{ langLabel(b.allowedLanguages) }} · {{ tr('{n} tests ({s} sample)', { n: b.testCount, s: b.sampleCount }) }}
              <span v-if="!b.mine"> · by {{ b.ownerName }}</span>
            </div>
            <div v-if="b.tags" class="flex flex-wrap gap-1 mt-1">
              <span v-for="t in b.tags.split(',')" :key="t"
                    class="text-[11px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300">{{ t.trim() }}</span>
            </div>
          </div>
          <button @click="add(b)" :disabled="busyId === b.id"
                  class="text-sm bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-1.5 disabled:opacity-50 shrink-0">
            {{ busyId === b.id ? "…" : "Add" }}
          </button>
        </div>
        <p v-if="!items.length" class="text-sm text-slate-500 dark:text-slate-400">{{ tr('Problem bank is empty.') }}</p>
      </div>
    </div>
  </div>
</template>
