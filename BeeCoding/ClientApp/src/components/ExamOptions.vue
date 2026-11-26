<script setup>
import { tr } from '../lib/i18n';

// The three exam switches, independent of each other: on a problem group, or on the board for ungrouped problems.
const props = defineProps({ modelValue: { type: Object, required: true } });   // { examMode, blockPaste, noAiHints }
const emit = defineEmits(['update:modelValue']);
const OPTIONS = [
  ['examMode', 'Hide other students’ work'],
  ['blockPaste', 'Block paste in the editor'],
  ['noAiHints', 'Switch off AI hints'],
];
const set = (key, on) => emit('update:modelValue', { ...props.modelValue, [key]: on });
</script>

<template>
  <div class="space-y-1.5">
    <label v-for="[key, label] in OPTIONS" :key="key" class="flex items-center gap-2 text-sm">
      <input type="checkbox" :checked="!!modelValue[key]" @change="set(key, $event.target.checked)" />
      {{ tr(label) }}
    </label>
  </div>
</template>
