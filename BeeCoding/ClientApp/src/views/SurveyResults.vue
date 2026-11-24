<script setup>
import { tr } from '../lib/i18n';
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import { QUESTIONS, SECTIONS } from '../lib/surveyQuestions';

const rows = ref([]);
const error = ref('');
const loading = ref(true);
const role = ref('all');

// Donut or bars for the multiple-choice questions (scales and NPS are ordered, so they always stay bars). A per-device preference.
const VIEW_KEY = 'beecoding.surveyResults.chart';
const view = ref((() => { try { return localStorage.getItem(VIEW_KEY) === 'bars' ? 'bars' : 'donut'; } catch { return 'donut'; } })());
function setView(v) { view.value = v; try { localStorage.setItem(VIEW_KEY, v); } catch { /* the choice just does not persist */ } }

const COLORS = ['#f59e0b', '#0ea5e9', '#10b981', '#8b5cf6', '#f43f5e', '#64748b'];
const R_ = 36;
// One ring segment per option. Each is its own arc path from angle to angle (clockwise from 12 o'clock), so nothing depends on
// where a circle's dash pattern starts: the earlier stroke-dasharray version drew a notch where a dash crossed the path's seam.
// A single option holding everything is a full circle, which an arc cannot draw (start = end).
function pt(frac) { const a = frac * 2 * Math.PI; return `${(50 + R_ * Math.sin(a)).toFixed(3)} ${(50 - R_ * Math.cos(a)).toFixed(3)}`; }
function segs(c) {
  let before = 0;
  return c.bars.map((b, i) => {
    const f = !c.n || !b.n ? 0 : b.n / c.n;
    const seg = { ...b, color: COLORS[i % COLORS.length], full: f >= 0.9999, d: f > 0 ? `M ${pt(before)} A ${R_} ${R_} 0 ${f > 0.5 ? 1 : 0} 1 ${pt(before + f)}` : '' };
    before += f;
    return seg;
  });
}

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
      <h1 class="text-2xl font-bold">{{ tr('Survey results') }}</h1>
      <span class="text-sm text-slate-500 dark:text-slate-400">{{ tr('{n} response(s)', { n: shown.length }) }}</span>
      <div class="ml-auto flex items-center gap-2">
        <select v-model="role" :aria-label="tr('Role')" class="border border-slate-300 dark:border-slate-600 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm">
          <option value="all">{{ tr('All') }}</option><option value="student">{{ tr('Students') }}</option><option value="teacher">{{ tr('Teachers') }}</option>
        </select>
        <div class="inline-flex rounded-md border border-slate-300 dark:border-slate-700 overflow-hidden text-xs" role="group" :aria-label="tr('Chart style')">
          <button v-for="[v, label] in [['donut', 'Donut'], ['bars', 'Bars']]" :key="v" type="button" @click="setView(v)" :aria-pressed="view === v"
                  class="px-2 py-1 font-medium" :class="view === v ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">{{ label }}</button>
        </div>
        <button @click="exportCsv" :disabled="!shown.length" class="row-action-btn">{{ tr('Export CSV') }}</button>
      </div>
    </div>
    <p class="text-xs text-slate-500 dark:text-slate-400 mb-6">{{ tr('Answers carry no name or email. With few responses in a group, treat the numbers as anecdotes.') }}</p>

    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
    <p v-else-if="loading" class="text-sm text-slate-500 dark:text-slate-400">…</p>
    <template v-else>
      <section v-for="s in SECTIONS" :key="s.id" class="mb-8">
        <h2 class="font-semibold text-sm uppercase tracking-wide text-amber-700 dark:text-amber-400 mb-3">{{ s.title.en }}</h2>
        <div class="space-y-3">
          <div v-for="c in cards.filter((x) => x.q.section === s.id)" :key="c.q.id"
               class="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-4">
            <div class="flex items-baseline gap-2">
              <h3 class="text-sm font-medium flex-1">{{ c.q.q.en }}</h3>
              <span v-if="c.mean !== undefined" class="text-xs tabular-nums text-slate-500 dark:text-slate-400">{{ tr('mean {n}', { n: c.mean }) }}</span>
              <span v-if="c.nps !== undefined" class="text-xs tabular-nums text-slate-500 dark:text-slate-400">NPS {{ c.nps }}</span>
              <span class="text-xs tabular-nums text-slate-500 dark:text-slate-400">n={{ c.n }}</span>
            </div>
            <div v-if="c.q.type === 'choice' && view === 'donut'" class="mt-3 flex flex-wrap items-center gap-x-6 gap-y-3">
              <svg viewBox="0 0 100 100" class="w-28 h-28 shrink-0" role="img" :aria-label="c.q.q.en">
                <circle cx="50" cy="50" :r="R_" fill="none" stroke-width="14" class="stroke-slate-100 dark:stroke-slate-800" />
                <template v-for="g in segs(c)" :key="g.label">
                  <circle v-if="g.full" cx="50" cy="50" :r="R_" fill="none" stroke-width="14" :stroke="g.color" />
                  <path v-else-if="g.d" :d="g.d" fill="none" stroke-width="14" :stroke="g.color" />
                </template>
                <text x="50" y="50" text-anchor="middle" dominant-baseline="central" class="fill-slate-700 dark:fill-slate-200 text-[16px] font-semibold">{{ c.n }}</text>
              </svg>
              <ul class="space-y-1 text-xs min-w-0 flex-1">
                <li v-for="g in segs(c)" :key="g.label" class="flex items-center gap-2">
                  <span class="w-2.5 h-2.5 rounded-sm shrink-0" :style="{ background: g.color }"></span>
                  <span class="flex-1 min-w-0 truncate text-slate-600 dark:text-slate-300" :title="g.label">{{ g.label }}</span>
                  <span class="tabular-nums text-slate-500 dark:text-slate-400">{{ g.n }} · {{ pct(g.n, c.n) }}%</span>
                </li>
              </ul>
            </div>
            <ul v-else-if="c.bars" class="mt-2 space-y-1">
              <li v-for="b in c.bars" :key="b.label" class="flex items-center gap-2 text-xs">
                <span class="w-28 shrink-0 truncate text-slate-600 dark:text-slate-300" :title="b.label">{{ b.label }}</span>
                <span class="flex-1 h-2 rounded bg-slate-100 dark:bg-slate-800 overflow-hidden"><span class="block h-full bg-amber-500" :style="{ width: pct(b.n, c.n) + '%' }"></span></span>
                <span class="w-14 text-right tabular-nums text-slate-500 dark:text-slate-400">{{ b.n }} · {{ pct(b.n, c.n) }}%</span>
              </li>
            </ul>
            <ul v-else class="mt-2 space-y-1.5">
              <li v-for="(t, i) in c.texts" :key="i" class="text-sm bg-slate-50 dark:bg-slate-800/60 rounded-lg px-3 py-2 whitespace-pre-wrap">{{ t }}</li>
              <li v-if="!c.texts.length" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('No answers yet.') }}</li>
            </ul>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>
