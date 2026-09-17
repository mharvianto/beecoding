<script setup>
const props = defineProps({
  pairs: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
});
const emit = defineEmits(['view', 'compare']);

function tier(score) {
  if (score >= 0.85) return 'text-rose-600 dark:text-rose-400 font-semibold';
  if (score >= 0.65) return 'text-amber-600 dark:text-amber-400 font-semibold';
  return 'text-slate-500 dark:text-slate-400';
}
</script>

<template>
  <div>
    <p class="text-xs text-slate-400 dark:text-slate-500 mb-3">
      Token-similarity between each pair of students' latest submission to the same problem — a spot-check
      signal, not proof of copying. Short/boilerplate submissions and pairs below 50% are left out.
    </p>
    <div v-if="loading" class="text-sm text-slate-400 dark:text-slate-500">Loading…</div>
    <div v-else-if="!pairs.length" class="text-sm text-slate-400 dark:text-slate-500">No similar pairs found.</div>
    <div v-else class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800">
      <div v-for="(p, i) in pairs" :key="i"
           class="flex items-center gap-3 px-4 py-2.5 bg-white dark:bg-slate-900 text-sm flex-wrap">
        <span class="w-16 shrink-0" :class="tier(p.similarity)">{{ Math.round(p.similarity * 100) }}%</span>
        <span class="text-slate-400 dark:text-slate-500 text-xs w-40 shrink-0 truncate" :title="p.problemTitle">{{ p.problemTitle }}</span>
        <button class="font-medium hover:underline" @click="emit('view', { id: p.submissionAId, authorName: p.userAName })">
          {{ p.userAName }}
        </button>
        <span class="text-slate-300 dark:text-slate-600">↔</span>
        <button class="font-medium hover:underline" @click="emit('view', { id: p.submissionBId, authorName: p.userBName })">
          {{ p.userBName }}
        </button>
        <button class="row-action-btn ml-auto" @click="emit('compare', p)">
          <span>⇄</span><span>Compare</span>
        </button>
      </div>
    </div>
  </div>
</template>
