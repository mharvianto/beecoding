<script setup>
import { tr } from '../lib/i18n';
import { theme, cycleTheme } from '../lib/theme';
import Icon from './Icon.vue';
const icon = { light: 'sun', dark: 'moon', system: 'monitor' };
</script>

<template>
  <button @click="cycleTheme" :title="tr('Theme: {t} (click to change)', { t: theme })" :aria-label="tr('Theme: {t}. Click to change.', { t: theme })"
          class="text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100">
    <Icon :name="icon[theme]" />
  </button>
</template>
