<script setup>
defineProps({ level: String });
const map = {
  Easy: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  Medium: 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
  Hard: 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300',
};
</script>

<template>
  <span v-if="level" class="inline-block text-[11px] font-semibold px-1.5 py-0.5 rounded"
        :class="map[level] || map.Medium">{{ level }}</span>
</template>
