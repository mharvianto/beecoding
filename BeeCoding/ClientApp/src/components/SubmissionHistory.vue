<script setup>
import { ref, computed, nextTick } from 'vue';
import VerdictBadge from './VerdictBadge.vue';
import { timeAgo, fullTime, lineDiff } from '../lib/timeAgo';

// Rows are buttons only for the viewer's own submissions that carry their code; a classmate's row (board) is plain text.
const props = defineProps({
  submissions: { type: Array, required: true },
  currentCode: { type: String, default: '' },   // what is in the editor now, to compare with
  showAuthor: { type: Boolean, default: false },
  emptyText: { type: String, default: 'No submissions yet.' },
});
const emit = defineEmits(['load']);

const canOpen = (s) => s.mine !== false && s.code != null;
const openId = ref(null);
const diff = ref(false);
const closeBtn = ref(null);
const open = computed(() => props.submissions.find((s) => s.id === openId.value));
const lines = computed(() => (open.value && diff.value ? lineDiff(open.value.code || '', props.currentCode) : null));

function show(s) { openId.value = s.id; diff.value = false; nextTick(() => closeBtn.value?.focus()); }
function close() { openId.value = null; }
function load() { emit('load', open.value); close(); }
</script>

<template>
  <div class="space-y-1 max-h-64 overflow-y-auto pr-1">
    <component :is="canOpen(s) ? 'button' : 'div'" v-for="s in submissions" :key="s.id"
               :type="canOpen(s) ? 'button' : undefined" @click="canOpen(s) && show(s)"
               class="w-full flex items-center gap-2 text-sm border border-slate-100 dark:border-slate-800 rounded-lg px-2 py-1.5 text-left"
               :class="canOpen(s) && 'hover:bg-slate-50 dark:hover:bg-slate-800/50 cursor-pointer'">
      <VerdictBadge :verdict="s.status === 'Done' ? s.verdict : s.status" small />
      <span v-if="showAuthor" class="text-slate-500 dark:text-slate-400">{{ s.authorName }}</span>
      <span v-if="s.status === 'Done'" class="text-xs text-slate-500 dark:text-slate-400">
        {{ s.runtimeMs }}ms · {{ s.memoryKb }}KB · {{ Math.round(s.score * 100) }}%
      </span>
      <time :datetime="s.createdAt" :title="fullTime(s.createdAt)" class="text-xs text-slate-500 dark:text-slate-400 ml-auto">{{ timeAgo(s.createdAt) }}</time>
    </component>
    <p v-if="!submissions.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ emptyText }}</p>
  </div>

  <div v-if="open" role="dialog" aria-modal="true" aria-label="Submission" @keydown.esc="close" @click.self="close"
       class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/50">
    <div class="w-full max-w-3xl max-h-[90vh] flex flex-col bg-white dark:bg-slate-900 rounded-2xl shadow-xl border border-slate-200 dark:border-slate-800 p-4 sm:p-5">
      <div class="flex items-center gap-2 mb-3">
        <VerdictBadge :verdict="open.status === 'Done' ? open.verdict : open.status" small />
        <span v-if="open.status === 'Done'" class="text-xs text-slate-500 dark:text-slate-400">{{ open.runtimeMs }}ms · {{ open.memoryKb }}KB · {{ Math.round(open.score * 100) }}%</span>
        <time :datetime="open.createdAt" class="text-xs text-slate-500 dark:text-slate-400">{{ fullTime(open.createdAt) }} ({{ timeAgo(open.createdAt) }})</time>
        <button ref="closeBtn" type="button" @click="close" aria-label="Close" class="ml-auto text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 px-1">✕</button>
      </div>

      <div class="flex flex-wrap items-center gap-2 mb-2">
        <div class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs" role="group">
          <button type="button" @click="diff = false" :aria-pressed="!diff" class="px-3 py-1" :class="!diff ? 'bg-amber-500 text-slate-900' : 'text-slate-600 dark:text-slate-300'">Code</button>
          <button type="button" @click="diff = true" :aria-pressed="diff" class="px-3 py-1" :class="diff ? 'bg-amber-500 text-slate-900' : 'text-slate-600 dark:text-slate-300'">Compare with editor</button>
        </div>
        <button type="button" @click="load" class="ml-auto text-sm bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-1.5">Load into editor</button>
      </div>

      <pre v-if="!diff" class="flex-1 min-h-0 text-xs bg-slate-50 dark:bg-slate-950 rounded p-3 overflow-auto whitespace-pre">{{ open.code }}</pre>
      <template v-else>
        <p class="text-[11px] text-slate-500 dark:text-slate-400 mb-1">This submission → your editor now. <span class="text-red-600 dark:text-red-400">− only in the submission</span> · <span class="text-emerald-600 dark:text-emerald-400">+ only in the editor</span></p>
        <pre v-if="lines" class="flex-1 min-h-0 text-xs bg-slate-50 dark:bg-slate-950 rounded p-3 overflow-auto whitespace-pre"><span v-for="(l, i) in lines" :key="i" class="block"
          :class="l.t === '+' ? 'bg-emerald-100 dark:bg-emerald-500/15' : l.t === '-' ? 'bg-red-100 dark:bg-red-500/15' : ''">{{ l.t }} {{ l.s }}</span></pre>
        <p v-else class="text-xs text-slate-500">Too long to compare (over 400 lines).</p>
      </template>
    </div>
  </div>
</template>
