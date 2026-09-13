<script setup>
import { tableView } from '../lib/tableView';
</script>

<template>
  <div class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs shrink-0">
    <button type="button" @click="tableView = 'card'" title="Card view"
            class="px-2 py-1"
            :class="tableView === 'card' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
      ▦ Cards
    </button>
    <button type="button" @click="tableView = 'table'" title="Table view"
            class="px-2 py-1 border-l border-slate-300 dark:border-slate-700"
            :class="tableView === 'table' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
      ☰ Table
    </button>
  </div>
</template>
