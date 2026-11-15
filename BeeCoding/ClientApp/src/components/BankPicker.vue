<script setup>
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
  <div class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-2xl p-5 my-8">
      <div class="flex items-center justify-between mb-3">
        <h2 class="font-bold text-lg">Add from problem bank</h2>
        <button @click="emit('cancel')" class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200">✕</button>
      </div>

      <div class="flex gap-2 mb-3">
        <input v-model="q" @keyup.enter="load" placeholder="Search title or tag…"
               class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
        <select v-model="scope" @change="load"
                class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
          <option value="all">All</option>
          <option value="mine">Mine</option>
          <option value="public">Shared</option>
        </select>
        <button @click="load" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-3">Search</button>
      </div>

      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400 mb-2">{{ error }}</p>

      <div class="space-y-2 max-h-[55vh] overflow-y-auto">
        <div v-for="b in items" :key="b.id"
             class="border border-slate-200 dark:border-slate-800 rounded-lg px-3 py-2 flex items-center gap-3">
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-1.5">
              <span class="font-medium text-sm truncate">{{ b.title }}</span>
              <LevelBadge :level="b.level" />
              <span v-if="b.generatedByAi" class="text-[10px] px-1 py-0.5 rounded-full bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">✨ AI</span>
            </div>
            <div class="text-[11px] text-slate-500 dark:text-slate-400">
              {{ langLabel(b.allowedLanguages) }} · {{ b.testCount }} tests ({{ b.sampleCount }} sample)
              <span v-if="!b.mine"> · by {{ b.ownerName }}</span>
              <span v-if="b.tags"> · {{ b.tags }}</span>
            </div>
          </div>
          <button @click="add(b)" :disabled="busyId === b.id"
                  class="text-sm bg-amber-500 text-white rounded-lg px-3 py-1 disabled:opacity-50 shrink-0">
            {{ busyId === b.id ? "…" : "Add" }}
          </button>
        </div>
        <p v-if="!items.length" class="text-sm text-slate-500 dark:text-slate-400">Problem bank is empty.</p>
      </div>
    </div>
  </div>
</template>
