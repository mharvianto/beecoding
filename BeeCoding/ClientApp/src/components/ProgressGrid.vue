<script setup>
import { ref, computed } from 'vue';
import VerdictBadge from './VerdictBadge.vue';
import SubmissionView from './SubmissionView.vue';

const props = defineProps({
  students: Array,
  problems: Array,
  cells: Array,
  isStaff: Boolean,
  currentUserId: Number,
});
const emit = defineEmits(['toggle-hide']);

const viewSubmissionId = ref(null);

const cellMap = computed(() => {
  const m = {};
  for (const c of props.cells) m[`${c.userId}:${c.problemId}`] = c;
  return m;
});

function cell(u, p) { return cellMap.value[`${u}:${p}`]; }

function solvedCount(userId) {
  return props.problems.filter((p) => cell(userId, p.id)?.latest).length;
}
</script>

<template>
  <div class="overflow-x-auto border border-slate-200 dark:border-slate-800 rounded-xl bg-white dark:bg-slate-900">
    <table class="w-full text-sm border-collapse">
      <thead>
        <tr class="bg-slate-50 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
          <th class="text-left font-medium px-3 py-2 sticky left-0 bg-slate-50 dark:bg-slate-800 z-10">Student</th>
          <th v-for="p in problems" :key="p.id" class="px-2 py-2 font-medium whitespace-nowrap">
            {{ p.title }}
          </th>
          <th class="px-3 py-2 font-medium">Solved</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="s in students" :key="s.userId" class="border-t border-slate-100 dark:border-slate-800">
          <td class="px-3 py-2 sticky left-0 bg-white dark:bg-slate-900 z-10 whitespace-nowrap">
            <span :class="{ 'font-semibold': s.userId === currentUserId }">{{ s.displayName }}</span>
            <span v-if="s.userId === currentUserId" class="text-xs text-slate-400 dark:text-slate-500"> (you)</span>
            <button v-if="isStaff" @click="emit('toggle-hide', s)"
                    class="ml-2 text-[10px] px-1.5 py-0.5 rounded border"
                    :class="s.hiddenByTeacher
                      ? 'bg-purple-100 text-purple-700 border-purple-200 dark:bg-purple-500/15 dark:text-purple-300 dark:border-purple-500/30'
                      : 'text-slate-400 dark:text-slate-500 border-slate-200 dark:border-slate-700 hover:border-slate-400'">
              {{ s.hiddenByTeacher ? 'hidden from peers' : 'visible' }}
            </button>
          </td>
          <td v-for="p in problems" :key="p.id" class="px-2 py-2 text-center">
            <template v-if="cell(s.userId, p.id)">
              <span v-if="cell(s.userId, p.id).redacted"
                    class="inline-block w-2.5 h-2.5 rounded-full bg-slate-300 dark:bg-slate-600" title="attempted (hidden)"></span>
              <button v-else-if="cell(s.userId, p.id).latestSubmissionId"
                      @click="viewSubmissionId = cell(s.userId, p.id).latestSubmissionId"
                      class="inline-flex p-0 m-0 leading-none rounded hover:ring-2 hover:ring-amber-400 transition" title="View submission">
                <VerdictBadge :verdict="cell(s.userId, p.id).verdict" small />
              </button>
              <VerdictBadge v-else :verdict="cell(s.userId, p.id).verdict" small />
              <div v-if="!cell(s.userId, p.id).redacted && cell(s.userId, p.id).attempts > 1"
                   class="text-[10px] text-slate-400 dark:text-slate-500">×{{ cell(s.userId, p.id).attempts }}</div>
            </template>
            <span v-else class="text-slate-200 dark:text-slate-700">·</span>
          </td>
          <td class="px-3 py-2 text-center font-semibold text-slate-600 dark:text-slate-300">
            {{ solvedCount(s.userId) }}/{{ problems.length }}
          </td>
        </tr>
        <tr v-if="!students.length">
          <td :colspan="problems.length + 2" class="px-3 py-6 text-center text-slate-400 dark:text-slate-500">
            No students yet.
          </td>
        </tr>
      </tbody>
    </table>

    <SubmissionView v-if="viewSubmissionId" :submission-id="viewSubmissionId" @close="viewSubmissionId = null" />
  </div>
</template>
