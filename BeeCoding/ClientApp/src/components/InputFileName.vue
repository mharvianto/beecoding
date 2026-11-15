<script setup>
// Optional file name for scratch runs. Blank = stdin is piped as usual; a name (e.g.
// input.txt) makes the run write the stdin box to that file so the program can fopen() it.
const model = defineModel({ type: String, default: '' });
</script>

<template>
  <label class="inline-flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400"
         title="Leave blank to pipe stdin. Set a name (e.g. input.txt) and the program reads it with fopen() instead.">
    read from file
    <input v-model.trim="model" maxlength="64" placeholder="stdin"
           class="w-24 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-1.5 py-0.5 font-mono text-slate-700 dark:text-slate-200" />
  </label>
</template>
