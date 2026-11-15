<script setup>
import { theme, cycleTheme } from '../lib/theme';
const icon = { light: '☀️', dark: '🌙', system: '🖥️' };
</script>

<template>
  <button @click="cycleTheme" :title="`Theme: ${theme} (click to change)`" :aria-label="`Theme: ${theme}. Click to change.`"
          class="text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100">
    {{ icon[theme] }}
  </button>
</template>
