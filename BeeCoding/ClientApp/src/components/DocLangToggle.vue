<script setup>
import { tr } from '../lib/i18n';
import { docLang, setDocLang } from '../lib/docLang';
</script>

<template>
  <div class="inline-flex rounded-md border border-slate-300 dark:border-slate-700 overflow-hidden text-xs" role="group" :aria-label="tr('Language')">
    <button v-for="[code, label, name] in [['en', 'EN', 'English'], ['id', 'ID', 'Bahasa Indonesia']]" :key="code"
            type="button" @click="setDocLang(code)" :title="name" :aria-pressed="docLang === code"
            class="px-2 py-1 font-medium"
            :class="docLang === code ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-100'">
      {{ label }}
    </button>
  </div>
</template>
