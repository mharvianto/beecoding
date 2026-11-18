<script setup>
import { ref, computed } from 'vue';
import VerdictBadge from './VerdictBadge.vue';
import { timeAgo, fullTime, lineDiff } from '../lib/timeAgo';

const props = defineProps({
  submissions: { type: Array, required: true },
  currentCode: { type: String, default: '' },   // what is in the editor now, to compare with
});
const emit = defineEmits(['load']);

const openId = ref(null);
const diff = ref(false);
const open = computed(() => props.submissions.find((s) => s.id === openId.value));
const lines = computed(() => (open.value && diff.value ? lineDiff(open.value.code || '', props.currentCode) : null));

function toggle(s) { openId.value = openId.value === s.id ? null : s.id; diff.value = false; }
</script>

<template>
  <div class="space-y-1 max-h-64 overflow-y-auto pr-1">
    <div v-for="s in submissions" :key="s.id" class="border border-slate-100 dark:border-slate-800 rounded-lg">
      <button type="button" @click="toggle(s)" :aria-expanded="openId === s.id"
              class="w-full flex items-center gap-2 text-sm px-2 py-1.5 text-left hover:bg-slate-50 dark:hover:bg-slate-800/50 rounded-lg">
        <VerdictBadge :verdict="s.status === 'Done' ? s.verdict : s.status" small />
        <span v-if="s.status === 'Done'" class="text-xs text-slate-500 dark:text-slate-400">
          {{ s.runtimeMs }}ms · {{ s.memoryKb }}KB · {{ Math.round(s.score * 100) }}%
        </span>
        <time :datetime="s.createdAt" :title="fullTime(s.createdAt)" class="text-xs text-slate-500 dark:text-slate-400 ml-auto">{{ timeAgo(s.createdAt) }}</time>
      </button>
      <div v-if="openId === s.id" class="px-2 pb-2">
        <div class="flex flex-wrap gap-2 mb-1.5">
          <button type="button" @click="emit('load', s)" class="row-action-btn row-action-btn--accent">Load into editor</button>
          <button type="button" @click="diff = !diff" class="row-action-btn" :aria-pressed="diff">{{ diff ? 'Show code' : 'Compare with editor' }}</button>
        </div>
        <pre v-if="!diff" class="text-xs bg-slate-50 dark:bg-slate-950 rounded p-2 overflow-auto max-h-60 whitespace-pre">{{ s.code }}</pre>
        <template v-else>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 mb-1">This submission → your editor now. <span class="text-red-600 dark:text-red-400">− only in the submission</span> · <span class="text-emerald-600 dark:text-emerald-400">+ only in the editor</span></p>
          <pre v-if="lines" class="text-xs bg-slate-50 dark:bg-slate-950 rounded p-2 overflow-auto max-h-60 whitespace-pre"><span v-for="(l, i) in lines" :key="i" class="block"
            :class="l.t === '+' ? 'bg-emerald-100 dark:bg-emerald-500/15' : l.t === '-' ? 'bg-red-100 dark:bg-red-500/15' : ''">{{ l.t }} {{ l.s }}</span></pre>
          <p v-else class="text-xs text-slate-500">Too long to compare (over 400 lines).</p>
        </template>
      </div>
    </div>
    <p v-if="!submissions.length" class="text-slate-500 dark:text-slate-400 text-sm">No submissions yet.</p>
  </div>
</template>
