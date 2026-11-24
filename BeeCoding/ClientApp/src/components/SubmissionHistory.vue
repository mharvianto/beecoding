<script setup>
import { vModal } from '../lib/vModal';
import { tr } from '../lib/i18n';
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
const sameCode = computed(() => {
  const a = open.value?.code, b = mode.value === 'prev' ? prev.value?.code : mode.value === 'editor' ? props.currentCode : null;
  return b != null && a === b;
});
const lang = computed(() => (open.value?.language === 'c' ? 'c' : 'cpp'));

const idx = computed(() => mine.value.findIndex((s) => s.id === openId.value));
const older = computed(() => mine.value[idx.value + 1]);
const newer = computed(() => (idx.value > 0 ? mine.value[idx.value - 1] : undefined));
const wide = ref(false);   // room for two columns: the modal is widened for a diff and Monaco keeps them side by side
function show(s) {
  wide.value = window.innerWidth >= 1200; openId.value = s.id; mode.value = 'code'; nextTick(() => closeBtn.value?.focus()); }
function close() { openId.value = null; }
function load() { emit('load', open.value); close(); }
</script>

<template>
  <p v-if="mine.length" class="text-[11px] text-slate-500 dark:text-slate-400 mb-1.5">{{ tr('Click a submission to see its code, compare it with your previous one, or load it into the editor.') }}</p>
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
      <span v-if="s.codePurged" class="text-[11px] text-slate-500 dark:text-slate-400" :title="tr('The source code of this submission was removed after the retention period. The verdict and score are kept.')">{{ tr('code removed') }}</span>
      <time :datetime="s.createdAt" :title="fullTime(s.createdAt)" class="text-xs text-slate-500 dark:text-slate-400 ml-auto">{{ timeAgo(s.createdAt) }}</time>
      <Chevron v-if="canOpen(s)" :open="false" size="1rem" class="text-slate-500 dark:text-slate-400" />
    </component>
    <p v-if="!submissions.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr(emptyText) }}</p>
  </div>

  <div v-if="open" role="dialog" aria-modal="true" v-modal="close" :aria-label="tr('Submission')" @click.self="close"
       class="fixed inset-0 z-50 flex items-start justify-center p-4 overflow-y-auto bg-black/50">
    <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full my-8 flex flex-col overflow-hidden transition-[max-width]" :class="mode === 'code' ? 'max-w-3xl' : 'max-w-6xl'" style="height: 80vh">
      <div class="border-b border-slate-100 dark:border-slate-800 shrink-0">
        <div class="flex items-center justify-between gap-2 px-4 pt-3">
          <div class="flex items-center gap-1.5 min-w-0">
            <button v-if="older" type="button" @click="show(older)" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 shrink-0 px-1" :title="tr('Older submission')" :aria-label="tr('Older submission')">◀</button>
            <VerdictBadge :verdict="open.status === 'Done' ? open.verdict : open.status" small />
            <time :datetime="open.createdAt" class="text-sm font-semibold truncate">{{ fullTime(open.createdAt) }} <span class="hidden sm:inline font-normal text-slate-500 dark:text-slate-400">({{ timeAgo(open.createdAt) }})</span></time>
            <button v-if="newer" type="button" @click="show(newer)" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 shrink-0 px-1" :title="tr('Newer submission')" :aria-label="tr('Newer submission')">▶</button>
          </div>
          <button ref="closeBtn" type="button" @click="close" :aria-label="tr('Close')" :title="tr('Close')" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 shrink-0">✕</button>
        </div>
        <div class="flex items-center gap-2 flex-wrap px-4 pb-2.5 pt-1 text-xs text-slate-500 dark:text-slate-400">
          <span v-if="open.status === 'Done'">{{ open.runtimeMs }}ms · {{ open.memoryKb }}KB · {{ Math.round(open.score * 100) }}%</span>
          <button v-if="prev" type="button" @click="mode = mode === 'prev' ? 'code' : 'prev'" :aria-pressed="mode === 'prev'"
                  class="row-action-btn shrink-0" :class="mode === 'prev' && 'row-action-btn--accent'"><span>⇄</span><span>{{ tr('Compare with previous') }}</span></button>
          <button type="button" @click="mode = mode === 'editor' ? 'code' : 'editor'" :aria-pressed="mode === 'editor'"
                  class="row-action-btn shrink-0" :class="mode === 'editor' && 'row-action-btn--accent'"><span>⇄</span><span>{{ tr('Compare with editor') }}</span></button>
          <button type="button" @click="load" class="shrink-0 ml-auto text-xs font-medium bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-3 py-1">{{ tr('Load into editor') }}</button>
        </div>
      </div>
      <p v-if="mode !== 'code'" class="shrink-0 text-[11px] text-slate-500 dark:text-slate-400 px-4 py-1 border-b border-slate-100 dark:border-slate-800">
        <template v-if="sameCode">{{ tr('Identical code — there is nothing to highlight.') }}</template>
        <template v-else-if="mode === 'prev'">{{ wide ? tr('Left: your previous submission ({when}) · Right: this one', { when: timeAgo(prev.createdAt) }) : tr('Red: your previous submission ({when}) · Green: this one', { when: timeAgo(prev.createdAt) }) }}</template>
        <template v-else>{{ wide ? tr('Left: this submission · Right: your editor now') : tr('Red: this submission · Green: your editor now') }}</template>
      </p>
      <div class="flex-1 min-h-0">
        <MonacoEditor v-if="mode === 'code'" :model-value="open.code" :language="lang" read-only :filename="'submission'" />
        <CodeDiff v-else-if="mode === 'prev'" :original="prev.code || ''" :modified="open.code || ''" :language="lang" />
        <CodeDiff v-else :original="open.code || ''" :modified="currentCode" :language="lang" />
      </div>
    </div>
  </div>
</template>
