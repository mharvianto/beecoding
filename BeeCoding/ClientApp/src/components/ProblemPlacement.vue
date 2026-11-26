<script setup>
import { tr } from '../lib/i18n';

// Where a new board problem goes, decided before it exists so students never see it in the wrong place.
// A problem for a group follows that group; one outside any group starts as a draft. Once the person ticks or
// unticks "draft" themselves, changing the group no longer touches it.
const props = defineProps({ modelValue: { type: Object, required: true }, groups: { type: Array, default: () => [] } });   // { groupId, hidden, chosen }
const emit = defineEmits(['update:modelValue']);
const setGroup = (v) => {
  const groupId = v === '' ? null : Number(v);
  emit('update:modelValue', { ...props.modelValue, groupId, hidden: props.modelValue.chosen ? props.modelValue.hidden : groupId === null });
};
const setHidden = (hidden) => emit('update:modelValue', { ...props.modelValue, hidden, chosen: true });
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
    <label class="flex items-center gap-2 min-w-0">
      <span class="text-slate-500 dark:text-slate-400">{{ tr('Group') }}</span>
      <select :value="modelValue.groupId ?? ''" @change="setGroup($event.target.value)"
              class="min-w-0 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1">
        <option value="">{{ tr('Ungrouped') }}</option>
        <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.title }}{{ g.hidden ? ` (${tr('draft')})` : '' }}</option>
      </select>
    </label>
    <label class="flex items-center gap-2">
      <input type="checkbox" :checked="modelValue.hidden" @change="setHidden($event.target.checked)" />
      {{ tr('Draft (not visible to students)') }}
    </label>
  </div>
</template>
