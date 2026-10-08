<script setup>
// A sortable <th>: shows the current direction on the active column and emits `sort` with its field.
defineProps({
  field: { type: String, required: true },
  label: { type: String, required: true },
  sort: { type: String, default: '' },     // active column
  dir: { type: String, default: 'asc' },   // 'asc' | 'desc'
});
defineEmits(['sort']);
</script>

<template>
  <th class="font-normal pr-3" :aria-sort="sort === field ? (dir === 'desc' ? 'descending' : 'ascending') : 'none'">
    <button type="button" @click="$emit('sort', field)" :title="`Sort by ${label}`"
            class="inline-flex items-center gap-1 hover:text-slate-700 dark:hover:text-slate-200"
            :class="sort === field ? 'text-slate-700 dark:text-slate-200' : ''">
      {{ label }}<span class="text-[9px]" aria-hidden="true">{{ sort === field ? (dir === 'desc' ? '▼' : '▲') : '' }}</span>
    </button>
  </th>
</template>
