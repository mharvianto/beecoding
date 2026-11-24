<script setup>
import { vModal } from '../lib/vModal';
import { tr } from '../lib/i18n';
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
    error.value = tr('The close time must be after the open time.'); return;
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
  <div role="dialog" aria-modal="true" v-modal="() => emit('close')" :aria-label="tr('Add several boards')" class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-2xl p-5 my-8 space-y-4">
      <div class="flex items-center justify-between">
        <h2 class="font-bold text-lg">{{ tr('Bulk add to') }} {{ tr(slugs.length === 1 ? '{n} board' : '{n} boards', { n: slugs.length }) }}</h2>
        <button @click="emit('close')" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" :aria-label="tr('Close')">✕</button>
      </div>

      <!-- groups -->
      <section class="space-y-2">
        <h3 class="text-sm font-semibold">{{ tr('1 · Groups') }} <span class="font-normal text-slate-500 dark:text-slate-400">{{ tr('(optional)') }}</span></h3>
        <textarea v-model="groupTitles" rows="3" placeholder="One group title per line&#10;Week 1&#10;Week 2" aria-label="One group title per line&#10;Week 1&#10;Week 2"
                  class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm font-mono"></textarea>
        <div class="grid grid-cols-2 gap-3">
          <label class="block text-xs text-slate-500 dark:text-slate-400">{{ tr('Opens') }}
            <input v-model="opensAt" type="datetime-local" class="mt-1 w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" />
          </label>
          <label class="block text-xs text-slate-500 dark:text-slate-400">{{ tr('Closes') }}
            <input v-model="closesAt" type="datetime-local" class="mt-1 w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" />
          </label>
        </div>
        <div class="flex gap-4 text-sm">
          <label class="flex items-center gap-1.5"><input v-model="examMode" type="checkbox" /> {{ tr('Exam mode') }}</label>
          <label class="flex items-center gap-1.5"><input v-model="hidden" type="checkbox" /> {{ tr('Hidden') }}</label>
        </div>
        <p class="text-[11px] text-slate-500 dark:text-slate-400">{{ tr('These settings apply to every new group. A group whose title already exists on a board is left untouched.') }}</p>
      </section>

      <!-- problems -->
      <section class="space-y-2">
        <h3 class="text-sm font-semibold">{{ tr('2 · Problems from the bank') }} <span class="font-normal text-slate-500 dark:text-slate-400">{{ tr('(optional)') }}</span></h3>
        <div class="flex gap-2">
          <input v-model="q" @keyup.enter="loadBank" :placeholder="tr('Search title or tag…')" :aria-label="tr('Search title or tag…')"
                 class="flex-1 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
          <button @click="loadBank" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-3">{{ tr('Search') }}</button>
        </div>
        <p v-if="bankError" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(bankError) }}</p>
        <div class="space-y-1 max-h-56 overflow-y-auto border border-slate-200 dark:border-slate-800 rounded-lg p-1">
          <label v-for="b in bank" :key="b.id"
                 class="flex items-center gap-2 px-2 py-1 rounded hover:bg-slate-50 dark:hover:bg-slate-800 cursor-pointer text-sm">
            <input type="checkbox" :checked="picked.has(b.id)" @change="togglePick(b.id)" />
            <span class="truncate">{{ b.title }}</span>
            <LevelBadge :level="b.level" />
            <span class="ml-auto text-[11px] text-slate-500 dark:text-slate-400 shrink-0">{{ b.ownerName }}</span>
          </label>
          <p v-if="!bank.length && !bankError" class="text-xs text-slate-500 dark:text-slate-400 px-2 py-2">{{ tr('No bank problems found.') }}</p>
        </div>
        <p class="text-xs text-slate-500 dark:text-slate-400">{{ tr('{n} selected', { n: picked.size }) }}</p>
        <label v-if="picked.size" class="block text-xs text-slate-500 dark:text-slate-400">{{ tr('Put them in group (optional — created if missing)') }}
          <input v-model="problemGroup" list="bulk-group-titles" :placeholder="tr('leave blank for ungrouped')" :aria-label="tr('Group')"
                 class="mt-1 w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
          <datalist id="bulk-group-titles"><option v-for="t in titles" :key="t" :value="t" /></datalist>
        </label>
      </section>

      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(error) }}</p>
      <div v-if="result" class="text-xs space-y-1 border border-slate-200 dark:border-slate-800 rounded-lg p-2 max-h-48 overflow-y-auto">
        <p class="font-medium text-emerald-600 dark:text-emerald-400">{{ tr('Updated {a} · failed {b}', { a: result.boardsUpdated, b: result.boardsFailed }) }}</p>
        <p v-for="r in result.rows" :key="r.slug" :class="r.error ? 'text-red-600 dark:text-red-400' : 'text-slate-500 dark:text-slate-400'">
          {{ r.title }}:
          <template v-if="r.error">{{ r.error }}</template>
          <template v-else>{{ tr('+{n} group(s)', { n: r.groupsAdded }) }}<template v-if="r.groupsSkipped"> {{ tr('({n} existed)', { n: r.groupsSkipped }) }}</template>,
            +{{ r.problemsAdded }} problem(s)<template v-if="r.problemsSkipped"> {{ tr('({n} already there)', { n: r.problemsSkipped }) }}</template></template>
        </p>
      </div>

      <div class="flex justify-end gap-2">
        <button @click="emit('close')" class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">
          {{ result ? tr('Close') : tr('Cancel') }}
        </button>
        <button @click="run" :disabled="busy || !canRun"
                class="px-4 py-1.5 rounded-lg text-sm font-medium text-white bg-amber-500 hover:bg-amber-600 disabled:opacity-50">
          {{ busy ? tr('Adding…') : `Add to ${slugs.length} board${slugs.length === 1 ? '' : 's'}` }}
        </button>
      </div>
    </div>
  </div>
</template>
