<script setup>
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import { QUESTIONS, SECTIONS } from '../lib/surveyQuestions';

const rows = ref([]);
const error = ref('');
const loading = ref(true);
const role = ref('all');

onMounted(async () => {
  try { rows.value = await api.get('/api/survey/results'); }
  catch (e) { error.value = e.message; }
  finally { loading.value = false; }
});

const shown = computed(() => rows.value.filter((r) => role.value === 'all' || r.role.toLowerCase() === role.value));
const num = (v) => (v === undefined ? NaN : Number(v));
const pct = (n, of) => (of ? Math.round((n / of) * 100) : 0);

const cards = computed(() => QUESTIONS.map((q) => {
  const vals = shown.value.map((r) => r.answers[q.id]).filter((v) => v !== undefined);
  const card = { q, n: vals.length };
  if (q.type === 'scale') {
    card.bars = [1, 2, 3, 4, 5].map((k) => ({ label: String(k), n: vals.filter((v) => num(v) === k).length }));
    card.mean = vals.length ? (vals.reduce((a, v) => a + num(v), 0) / vals.length).toFixed(2) : '–';
  } else if (q.type === 'nps') {
    const s = vals.map(num);
    card.bars = Array.from({ length: 11 }, (_, k) => ({ label: String(k), n: s.filter((v) => v === k).length }));
    card.nps = s.length ? Math.round(((s.filter((v) => v >= 9).length - s.filter((v) => v <= 6).length) / s.length) * 100) : '–';
  } else if (q.type === 'choice') {
    card.bars = q.options.map(([value, en]) => ({ label: en, n: vals.filter((v) => v === value).length }));
  } else card.texts = vals;
  return card;
}));

function csvCell(v) { const s = String(v ?? ''); return /[",\n]/.test(s) ? `"${s.replaceAll('"', '""')}"` : s; }
function exportCsv() {
  const head = ['role', 'updated', ...QUESTIONS.map((q) => q.id)];
  const lines = [head, ...shown.value.map((r) => [r.role, r.at, ...QUESTIONS.map((q) => r.answers[q.id] ?? '')])].map((l) => l.map(csvCell).join(','));
  const url = URL.createObjectURL(new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }));
  const a = Object.assign(document.createElement('a'), { href: url, download: 'survey-results.csv' });
  a.click(); URL.revokeObjectURL(url);
}
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-8">
    <div class="flex flex-wrap items-center gap-3 mb-1">
      <h1 class="text-2xl font-bold">Survey results</h1>
      <span class="text-sm text-slate-500 dark:text-slate-400">{{ shown.length }} response(s)</span>
      <div class="ml-auto flex items-center gap-2">
        <select v-model="role" aria-label="Role" class="border border-slate-300 dark:border-slate-600 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm">
          <option value="all">All</option><option value="student">Students</option><option value="teacher">Teachers</option>
        </select>
        <button @click="exportCsv" :disabled="!shown.length" class="row-action-btn">Export CSV</button>
      </div>
    </div>
    <p class="text-xs text-slate-500 dark:text-slate-400 mb-6">Answers carry no name or email. With few responses in a group, treat the numbers as anecdotes.</p>

    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
    <p v-else-if="loading" class="text-sm text-slate-500">…</p>
    <template v-else>
      <section v-for="s in SECTIONS" :key="s.id" class="mb-8">
        <h2 class="font-semibold text-sm uppercase tracking-wide text-amber-700 dark:text-amber-400 mb-3">{{ s.title.en }}</h2>
        <div class="space-y-3">
          <div v-for="c in cards.filter((x) => x.q.section === s.id)" :key="c.q.id"
               class="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-4">
            <div class="flex items-baseline gap-2">
              <h3 class="text-sm font-medium flex-1">{{ c.q.q.en }}</h3>
              <span v-if="c.mean !== undefined" class="text-xs tabular-nums text-slate-500">mean {{ c.mean }}</span>
              <span v-if="c.nps !== undefined" class="text-xs tabular-nums text-slate-500">NPS {{ c.nps }}</span>
              <span class="text-xs tabular-nums text-slate-400">n={{ c.n }}</span>
            </div>
            <ul v-if="c.bars" class="mt-2 space-y-1">
              <li v-for="b in c.bars" :key="b.label" class="flex items-center gap-2 text-xs">
                <span class="w-28 shrink-0 truncate text-slate-600 dark:text-slate-300" :title="b.label">{{ b.label }}</span>
                <span class="flex-1 h-2 rounded bg-slate-100 dark:bg-slate-800 overflow-hidden"><span class="block h-full bg-amber-500" :style="{ width: pct(b.n, c.n) + '%' }"></span></span>
                <span class="w-14 text-right tabular-nums text-slate-500">{{ b.n }} · {{ pct(b.n, c.n) }}%</span>
              </li>
            </ul>
            <ul v-else class="mt-2 space-y-1.5">
              <li v-for="(t, i) in c.texts" :key="i" class="text-sm bg-slate-50 dark:bg-slate-800/60 rounded-lg px-3 py-2 whitespace-pre-wrap">{{ t }}</li>
              <li v-if="!c.texts.length" class="text-xs text-slate-400">No answers yet.</li>
            </ul>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>
