<script setup>
const props = defineProps({ verdict: String, small: Boolean });

const map = {
  Accepted: ['bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300', 'AC'],
  WrongAnswer: ['bg-red-100 text-red-700 dark:bg-red-500/15 dark:text-red-300', 'WA'],
  TimeLimit: ['bg-orange-100 text-orange-700 dark:bg-orange-500/15 dark:text-orange-300', 'TLE'],
  MemoryLimit: ['bg-purple-100 text-purple-700 dark:bg-purple-500/15 dark:text-purple-300', 'MLE'],
  RuntimeError: ['bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300', 'RE'],
  CompileError: ['bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-200', 'CE'],
  Queued: ['bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400', '…'],
  Running: ['bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300', 'run'],
  Hidden: ['bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400', '•'],
  None: ['bg-slate-50 text-slate-300 dark:bg-slate-800 dark:text-slate-600', '–'],
};
</script>

<template>
  <span class="inline-flex items-center justify-center rounded font-semibold"
        :class="[map[verdict]?.[0] || map.None[0], small ? 'text-[11px] px-1.5 py-0.5 min-w-[34px]' : 'text-xs px-2 py-1']">
    {{ map[verdict]?.[1] || verdict }}
  </span>
</template>
