<script setup>
import { computed, ref } from 'vue';

const props = defineProps({
  result: { type: Object, default: () => ({ groups: [], pairs: [] }) },
  loading: { type: Boolean, default: false },
  idle: { type: Boolean, default: false },   // no board chosen yet: say so instead of "nothing found"
});
const emit = defineEmits(['view', 'compare']);

function tier(score) {
  if (score >= 0.85) return 'text-rose-600 dark:text-rose-400 font-semibold';
  if (score >= 0.65) return 'text-amber-700 dark:text-amber-400 font-semibold';
  return 'text-slate-500 dark:text-slate-400';
}
const pct = (v) => `${Math.round(v * 100)}%`;

// One section per problem (strongest first): its groups of 3+ students, then the pairs that belong to no group.
const sections = computed(() => {
  const by = new Map();
  const sec = (id, title) => {
    if (!by.has(id)) by.set(id, { id, title, groups: [], pairs: [], top: 0 });
    return by.get(id);
  };
  for (const g of props.result?.groups ?? []) { const s = sec(g.problemId, g.problemTitle); s.groups.push(g); s.top = Math.max(s.top, g.maxSimilarity); }
  for (const p of props.result?.pairs ?? []) { const s = sec(p.problemId, p.problemTitle); s.pairs.push(p); s.top = Math.max(s.top, p.similarity); }
  return [...by.values()].sort((a, b) => b.top - a.top);
});

const opened = ref({});
const groupKey = (g) => `${g.problemId}:${g.members.map((m) => m.userId).join(',')}`;
const toggle = (g) => { opened.value[groupKey(g)] = !opened.value[groupKey(g)]; };
</script>

<template>
  <div>
    <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
      Token-similarity between students' latest submissions to the same problem — a spot-check signal, not proof of copying.
      Three or more students linked at 65% or more are shown as one group (A~B and B~C put A, B and C together, so check the lowest
      figure: a low one means some members are only linked through another). Short/boilerplate submissions and pairs below 50% are left out.
    </p>
    <div v-if="loading" class="text-sm text-slate-500 dark:text-slate-400">Loading…</div>
    <div v-else-if="idle" class="text-sm text-slate-500 dark:text-slate-400">Pick a board above to compare its students' code.</div>
    <div v-else-if="!sections.length" class="text-sm text-slate-500 dark:text-slate-400">No similar pairs found.</div>
    <div v-else class="space-y-5">
      <section v-for="s in sections" :key="s.id">
        <h3 v-if="sections.length > 1" class="text-sm font-semibold mb-2">{{ s.title }}</h3>

        <div v-for="g in s.groups" :key="groupKey(g)"
             class="border border-slate-200 dark:border-slate-800 rounded-xl bg-white dark:bg-slate-900 p-3 mb-2 text-sm">
          <div class="flex items-center gap-3 flex-wrap">
            <span class="font-semibold">{{ g.members.length }} students</span>
            <span class="text-xs text-slate-500 dark:text-slate-400">
              highest <span :class="tier(g.maxSimilarity)">{{ pct(g.maxSimilarity) }}</span> ·
              average {{ pct(g.avgSimilarity) }} ·
              lowest <span :class="tier(g.minSimilarity)">{{ pct(g.minSimilarity) }}</span>
            </span>
            <button class="row-action-btn ml-auto" @click="toggle(g)">
              <span>{{ opened[groupKey(g)] ? '▾' : '▸' }}</span><span>Strongest pairs</span>
            </button>
          </div>
          <div class="flex flex-wrap gap-1.5 mt-2">
            <button v-for="m in g.members" :key="m.userId" @click="emit('view', { id: m.submissionId, authorName: m.userName })"
                    class="px-2 py-0.5 rounded-full border border-slate-300 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
              {{ m.userName }}
            </button>
          </div>
          <div v-if="opened[groupKey(g)]" class="mt-2 divide-y divide-slate-100 dark:divide-slate-800">
            <div v-for="(p, i) in g.strongestPairs" :key="i" class="flex items-center gap-3 py-1.5 flex-wrap">
              <span class="w-12 shrink-0" :class="tier(p.similarity)">{{ pct(p.similarity) }}</span>
              <span class="font-medium">{{ p.userAName }}</span>
              <span class="text-slate-300 dark:text-slate-600">↔</span>
              <span class="font-medium">{{ p.userBName }}</span>
              <button class="row-action-btn ml-auto" @click="emit('compare', p)"><span>⇄</span><span>Compare</span></button>
            </div>
          </div>
        </div>

        <div v-if="s.pairs.length" class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800">
          <div v-for="(p, i) in s.pairs" :key="i"
               class="flex items-center gap-3 px-4 py-2.5 bg-white dark:bg-slate-900 text-sm flex-wrap">
            <span class="w-16 shrink-0" :class="tier(p.similarity)">{{ pct(p.similarity) }}</span>
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
      </section>
    </div>
  </div>
</template>
