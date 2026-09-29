<script setup>
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import LevelBadge from './LevelBadge.vue';

// Admin / org-admin "bulk add": create the same groups and/or copy the same bank problems
// into every selected board. Re-running is safe — groups that already exist (by title) and
// problems already copied are skipped per board.
const props = defineProps({
  slugs: { type: Array, required: true },
  bankUrl: { type: String, required: true },   // GET, ?q= — bank problems this actor may copy
  addUrl: { type: String, required: true },    // POST BulkAddDto
});
const emit = defineEmits(['done', 'close']);

const groupTitles = ref('');
const examMode = ref(false);
const hidden = ref(false);
const opensAt = ref('');
const closesAt = ref('');
const problemGroup = ref('');

const q = ref('');
const bank = ref([]);
const picked = ref(new Set());
const bankError = ref('');
async function loadBank() {
  bankError.value = '';
  try {
    const p = new URLSearchParams();
    if (q.value.trim()) p.set('q', q.value.trim());
    bank.value = await api.get(`${props.bankUrl}?${p}`);
  } catch (e) { bankError.value = e.message; }
}
onMounted(loadBank);
function togglePick(id) {
  const next = new Set(picked.value);
  next.has(id) ? next.delete(id) : next.add(id);
  picked.value = next;
}

const titles = computed(() => groupTitles.value.split('\n').map((t) => t.trim()).filter(Boolean));
const iso = (v) => (v ? new Date(v).toISOString() : null);
const canRun = computed(() => props.slugs.length && (titles.value.length || picked.value.size));

const busy = ref(false);
const error = ref('');
const result = ref(null);
async function run() {
  error.value = ''; result.value = null;
  if (opensAt.value && closesAt.value && new Date(closesAt.value) <= new Date(opensAt.value)) {
    error.value = 'The close time must be after the open time.'; return;
  }
  busy.value = true;
  try {
    result.value = await api.post(props.addUrl, {
      slugs: props.slugs,
      groups: titles.value.map((title) => ({
        title, hidden: hidden.value, examMode: examMode.value, opensAt: iso(opensAt.value), closesAt: iso(closesAt.value),
      })),
      bankProblemIds: [...picked.value],
      problemGroupTitle: problemGroup.value.trim() || null,
    });
    emit('done');
  } catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}
</script>

<template>
  <div class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-2xl p-5 my-8 space-y-4">
      <div class="flex items-center justify-between">
        <h2 class="font-bold text-lg">Bulk add to {{ slugs.length }} board{{ slugs.length === 1 ? '' : 's' }}</h2>
        <button @click="emit('close')" class="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200">✕</button>
      </div>

      <!-- groups -->
      <section class="space-y-2">
        <h3 class="text-sm font-semibold">1 · Groups <span class="font-normal text-slate-400 dark:text-slate-500">(optional)</span></h3>
        <textarea v-model="groupTitles" rows="3" placeholder="One group title per line&#10;Week 1&#10;Week 2"
                  class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm font-mono"></textarea>
        <div class="grid grid-cols-2 gap-3">
          <label class="block text-xs text-slate-500 dark:text-slate-400">Opens
            <input v-model="opensAt" type="datetime-local" class="mt-1 w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" />
          </label>
          <label class="block text-xs text-slate-500 dark:text-slate-400">Closes
            <input v-model="closesAt" type="datetime-local" class="mt-1 w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" />
          </label>
        </div>
        <div class="flex gap-4 text-sm">
          <label class="flex items-center gap-1.5"><input v-model="examMode" type="checkbox" /> Exam mode</label>
          <label class="flex items-center gap-1.5"><input v-model="hidden" type="checkbox" /> Hidden</label>
        </div>
        <p class="text-[11px] text-slate-400 dark:text-slate-500">These settings apply to every new group. A group whose title already exists on a board is left untouched.</p>
      </section>

      <!-- problems -->
      <section class="space-y-2">
        <h3 class="text-sm font-semibold">2 · Problems from the bank <span class="font-normal text-slate-400 dark:text-slate-500">(optional)</span></h3>
        <div class="flex gap-2">
          <input v-model="q" @keyup.enter="loadBank" placeholder="Search title or tag…"
                 class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
          <button @click="loadBank" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-3">Search</button>
        </div>
        <p v-if="bankError" class="text-sm text-red-600 dark:text-red-400">{{ bankError }}</p>
        <div class="space-y-1 max-h-56 overflow-y-auto border border-slate-200 dark:border-slate-800 rounded-lg p-1">
          <label v-for="b in bank" :key="b.id"
                 class="flex items-center gap-2 px-2 py-1 rounded hover:bg-slate-50 dark:hover:bg-slate-800 cursor-pointer text-sm">
            <input type="checkbox" :checked="picked.has(b.id)" @change="togglePick(b.id)" />
            <span class="truncate">{{ b.title }}</span>
            <LevelBadge :level="b.level" />
            <span class="ml-auto text-[11px] text-slate-400 dark:text-slate-500 shrink-0">{{ b.ownerName }}</span>
          </label>
          <p v-if="!bank.length && !bankError" class="text-xs text-slate-400 dark:text-slate-500 px-2 py-2">No bank problems found.</p>
        </div>
        <p class="text-xs text-slate-500 dark:text-slate-400">{{ picked.size }} selected</p>
        <label v-if="picked.size" class="block text-xs text-slate-500 dark:text-slate-400">Put them in group (optional — created if missing)
          <input v-model="problemGroup" list="bulk-group-titles" placeholder="leave blank for ungrouped"
                 class="mt-1 w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
          <datalist id="bulk-group-titles"><option v-for="t in titles" :key="t" :value="t" /></datalist>
        </label>
      </section>

      <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      <div v-if="result" class="text-xs space-y-1 border border-slate-200 dark:border-slate-800 rounded-lg p-2 max-h-48 overflow-y-auto">
        <p class="font-medium text-emerald-600 dark:text-emerald-400">Updated {{ result.boardsUpdated }} · failed {{ result.boardsFailed }}</p>
        <p v-for="r in result.rows" :key="r.slug" :class="r.error ? 'text-red-600 dark:text-red-400' : 'text-slate-500 dark:text-slate-400'">
          {{ r.title }}:
          <template v-if="r.error">{{ r.error }}</template>
          <template v-else>+{{ r.groupsAdded }} group(s)<template v-if="r.groupsSkipped"> ({{ r.groupsSkipped }} existed)</template>,
            +{{ r.problemsAdded }} problem(s)<template v-if="r.problemsSkipped"> ({{ r.problemsSkipped }} already there)</template></template>
        </p>
      </div>

      <div class="flex justify-end gap-2">
        <button @click="emit('close')" class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">
          {{ result ? 'Close' : 'Cancel' }}
        </button>
        <button @click="run" :disabled="busy || !canRun"
                class="px-4 py-1.5 rounded-lg text-sm font-medium text-white bg-amber-500 hover:bg-amber-600 disabled:opacity-50">
          {{ busy ? 'Adding…' : `Add to ${slugs.length} board${slugs.length === 1 ? '' : 's'}` }}
        </button>
      </div>
    </div>
  </div>
</template>
