<script setup>
import ExamOptions from './ExamOptions.vue';
import { vModal } from '../lib/vModal';
import { tr } from '../lib/i18n';
import { ref } from 'vue';

// Create/edit a problem group (folder/session): title, hide flag, the exam switches and an optional
// open/close window. Times are edited in the viewer's local zone and sent as UTC instants.
const props = defineProps({ group: { type: Object, default: null } });
const emit = defineEmits(['save', 'cancel']);

const pad = (n) => String(n).padStart(2, '0');
function toLocalInput(iso) {
  if (!iso) return '';
  const d = new Date(iso.endsWith('Z') ? iso : iso + 'Z');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
const fromLocalInput = (v) => (v ? new Date(v).toISOString() : null);

const title = ref(props.group?.title || '');
const hidden = ref(!!props.group?.hidden);
const exam = ref({ examMode: !!props.group?.examMode, blockPaste: !!props.group?.blockPaste, noAiHints: !!props.group?.noAiHints });
const archived = ref(!!props.group?.archived);
const opensAt = ref(toLocalInput(props.group?.opensAt));
const closesAt = ref(toLocalInput(props.group?.closesAt));
const error = ref('');

function save() {
  if (!title.value.trim()) { error.value = tr('Give the group a title.'); return; }
  if (opensAt.value && closesAt.value && new Date(closesAt.value) <= new Date(opensAt.value)) {
    error.value = tr('The close time must be after the open time.'); return;
  }
  emit('save', {
    title: title.value.trim(),
    hidden: hidden.value,
    ...exam.value,
    archived: archived.value,
    opensAt: fromLocalInput(opensAt.value),
    closesAt: fromLocalInput(closesAt.value),
  });
}
</script>

<template>
  <div role="dialog" aria-modal="true" v-modal="() => emit('cancel')" :aria-label="tr('Edit group')" class="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50" @click.self="emit('cancel')">
    <form @submit.prevent="save"
          class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-md p-5 shadow-lg space-y-3">
      <h2 class="font-semibold">{{ group ? tr('Edit group') : tr('New group') }}</h2>

      <label class="block text-sm">
        <span class="text-slate-500 dark:text-slate-400">{{ tr('Title') }}</span>
        <input v-model="title" maxlength="120" autofocus :placeholder="tr('e.g. Week 5 — Arrays')" :aria-label="tr('Group title')"
               class="mt-1 w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
      </label>

      <div class="grid grid-cols-2 gap-3">
        <label class="block text-sm">
          <span class="text-slate-500 dark:text-slate-400">{{ tr('Opens (optional)') }}</span>
          <input v-model="opensAt" type="datetime-local"
                 class="mt-1 w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-sm" />
        </label>
        <label class="block text-sm">
          <span class="text-slate-500 dark:text-slate-400">{{ tr('Closes (optional)') }}</span>
          <input v-model="closesAt" type="datetime-local"
                 class="mt-1 w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-sm" />
        </label>
      </div>
      <p class="text-xs text-slate-500 dark:text-slate-400">
        {{ tr('Before it opens, students can\'t see the group. After it closes they can still read the problems but can\'t submit.') }}
      </p>

      <ExamOptions v-model="exam" />
      <label class="flex items-start gap-2 text-sm">
        <input v-model="archived" type="checkbox" class="mt-1" />
        <span>{{ tr('Archived') }} <span class="text-slate-500 dark:text-slate-400">{{ tr('— moved to the archive; students can still read and submit, even after the close time.') }}</span></span>
      </label>
      <label class="flex items-start gap-2 text-sm">
        <input v-model="hidden" type="checkbox" class="mt-1" />
        <span>{{ tr('Hidden') }} <span class="text-slate-500 dark:text-slate-400">{{ tr('— keep it as a draft, invisible to students.') }}</span></span>
      </label>

      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(error) }}</p>
      <div class="flex justify-end gap-2 pt-1">
        <button type="button" @click="emit('cancel')"
                class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">{{ tr('Cancel') }}</button>
        <button type="submit" class="px-3 py-1.5 rounded-lg text-sm font-medium text-white bg-amber-500 hover:bg-amber-600">{{ tr('Save') }}</button>
      </div>
    </form>
  </div>
</template>
