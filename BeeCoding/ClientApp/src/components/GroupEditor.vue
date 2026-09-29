<script setup>
import { ref } from 'vue';

// Create/edit a problem group (folder/session): title, hide flag, exam mode and an optional
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
const examMode = ref(!!props.group?.examMode);
const opensAt = ref(toLocalInput(props.group?.opensAt));
const closesAt = ref(toLocalInput(props.group?.closesAt));
const error = ref('');

function save() {
  if (!title.value.trim()) { error.value = 'Give the group a title.'; return; }
  if (opensAt.value && closesAt.value && new Date(closesAt.value) <= new Date(opensAt.value)) {
    error.value = 'The close time must be after the open time.'; return;
  }
  emit('save', {
    title: title.value.trim(),
    hidden: hidden.value,
    examMode: examMode.value,
    opensAt: fromLocalInput(opensAt.value),
    closesAt: fromLocalInput(closesAt.value),
  });
}
</script>

<template>
  <div class="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50" @keydown.esc="emit('cancel')" @click.self="emit('cancel')">
    <form @submit.prevent="save"
          class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-md p-5 shadow-lg space-y-3">
      <h2 class="font-semibold">{{ group ? 'Edit group' : 'New group' }}</h2>

      <label class="block text-sm">
        <span class="text-slate-500 dark:text-slate-400">Title</span>
        <input v-model="title" maxlength="120" autofocus placeholder="e.g. Week 5 — Arrays"
               class="mt-1 w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
      </label>

      <div class="grid grid-cols-2 gap-3">
        <label class="block text-sm">
          <span class="text-slate-500 dark:text-slate-400">Opens (optional)</span>
          <input v-model="opensAt" type="datetime-local"
                 class="mt-1 w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-sm" />
        </label>
        <label class="block text-sm">
          <span class="text-slate-500 dark:text-slate-400">Closes (optional)</span>
          <input v-model="closesAt" type="datetime-local"
                 class="mt-1 w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-sm" />
        </label>
      </div>
      <p class="text-xs text-slate-400 dark:text-slate-500">
        Before it opens, students can't see the group. After it closes they can still read the problems but can't submit.
      </p>

      <label class="flex items-start gap-2 text-sm">
        <input v-model="examMode" type="checkbox" class="mt-1" />
        <span>Exam mode <span class="text-slate-400 dark:text-slate-500">— students can't see peers' work on these problems, and paste is blocked.</span></span>
      </label>
      <label class="flex items-start gap-2 text-sm">
        <input v-model="hidden" type="checkbox" class="mt-1" />
        <span>Hidden <span class="text-slate-400 dark:text-slate-500">— keep it as a draft, invisible to students.</span></span>
      </label>

      <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      <div class="flex justify-end gap-2 pt-1">
        <button type="button" @click="emit('cancel')"
                class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">Cancel</button>
        <button type="submit" class="px-3 py-1.5 rounded-lg text-sm font-medium text-white bg-amber-500 hover:bg-amber-600">Save</button>
      </div>
    </form>
  </div>
</template>
