<script setup>
import { ref, computed, nextTick } from 'vue';
import VerdictBadge from './VerdictBadge.vue';
import MonacoEditor from './MonacoEditor.vue';
import CodeDiff from './CodeDiff.vue';
import Chevron from './Chevron.vue';
import { timeAgo, fullTime } from '../lib/timeAgo';

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
const mode = ref('code');   // 'code' | 'prev' (against my attempt just before) | 'editor' (against what is in the editor now)
const closeBtn = ref(null);
const mine = computed(() => props.submissions.filter(canOpen));   // newest first
const open = computed(() => props.submissions.find((s) => s.id === openId.value));
const prev = computed(() => { const i = mine.value.findIndex((s) => s.id === openId.value); return i >= 0 ? mine.value[i + 1] : undefined; });
const lang = computed(() => (open.value?.language === 'c' ? 'c' : 'cpp'));

function show(s) { openId.value = s.id; mode.value = 'code'; nextTick(() => closeBtn.value?.focus()); }
function close() { openId.value = null; }
function load() { emit('load', open.value); close(); }
</script>

<template>
  <p v-if="mine.length" class="text-[11px] text-slate-500 dark:text-slate-400 mb-1.5">Click a submission to see its code, compare it with your previous one, or load it into the editor.</p>
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
      <Chevron v-if="canOpen(s)" :open="false" size="1rem" class="text-slate-400" />
    </component>
    <p v-if="!submissions.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ emptyText }}</p>
  </div>

  <div v-if="open" role="dialog" aria-modal="true" aria-label="Submission" @keydown.esc="close" @click.self="close"
       class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/50">
    <div class="w-full max-w-5xl max-h-[95vh] flex flex-col bg-white dark:bg-slate-900 rounded-2xl shadow-xl border border-slate-200 dark:border-slate-800 p-4 sm:p-5">
      <div class="flex items-center gap-2 mb-3">
        <VerdictBadge :verdict="open.status === 'Done' ? open.verdict : open.status" small />
        <span v-if="open.status === 'Done'" class="text-xs text-slate-500 dark:text-slate-400">{{ open.runtimeMs }}ms · {{ open.memoryKb }}KB · {{ Math.round(open.score * 100) }}%</span>
        <time :datetime="open.createdAt" class="text-xs text-slate-500 dark:text-slate-400">{{ fullTime(open.createdAt) }} ({{ timeAgo(open.createdAt) }})</time>
        <button ref="closeBtn" type="button" @click="close" aria-label="Close" class="ml-auto text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 px-1">✕</button>
      </div>

      <div class="flex flex-wrap items-center gap-2 mb-2">
        <div class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs" role="group">
          <button type="button" @click="mode = 'code'" :aria-pressed="mode === 'code'" class="px-3 py-1" :class="mode === 'code' ? 'bg-amber-500 text-slate-900' : 'text-slate-600 dark:text-slate-300'">Code</button>
          <button type="button" @click="mode = 'prev'" :disabled="!prev" :aria-pressed="mode === 'prev'" class="px-3 py-1 disabled:opacity-40" :class="mode === 'prev' ? 'bg-amber-500 text-slate-900' : 'text-slate-600 dark:text-slate-300'"
                  :title="prev ? '' : 'This is your first submission'">vs previous submission</button>
          <button type="button" @click="mode = 'editor'" :aria-pressed="mode === 'editor'" class="px-3 py-1" :class="mode === 'editor' ? 'bg-amber-500 text-slate-900' : 'text-slate-600 dark:text-slate-300'">vs editor now</button>
        </div>
        <button type="button" @click="load" class="ml-auto text-sm bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-1.5">Load into editor</button>
      </div>
      <p v-if="mode !== 'code'" class="text-[11px] text-slate-500 dark:text-slate-400 mb-1">
        {{ mode === 'prev' ? `Left: your previous submission (${timeAgo(prev.createdAt)}) · Right: this one` : 'Left: this submission · Right: your editor now' }}
      </p>

      <div class="h-[60vh]">
        <MonacoEditor v-if="mode === 'code'" :model-value="open.code" :language="lang" read-only :filename="'submission'" />
        <CodeDiff v-else-if="mode === 'prev'" :original="prev.code || ''" :modified="open.code || ''" :language="lang" />
        <CodeDiff v-else :original="open.code || ''" :modified="currentCode" :language="lang" />
      </div>
    </div>
  </div>
</template>
