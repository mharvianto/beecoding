<script setup>
import { ref, computed } from 'vue';
import VerdictBadge from './VerdictBadge.vue';
import SubmissionView from './SubmissionView.vue';
import { LETTER_THRESHOLD, letter } from '../lib/problemLabel';

const props = defineProps({
  students: Array,
  problems: Array,
  cells: Array,
  hints: { type: Array, default: () => [] },   // staff only: [{ userId, problemId, level, lastAt }] — AI tutor hint depth right now
  isStaff: Boolean,
  canRemove: Boolean,   // board owner: may remove a student from the board
  currentUserId: Number,
  boardSlug: { type: String, default: '' },   // lets a problem's column header open that problem
});
const emit = defineEmits(['toggle-hide', 'remove']);

const viewSubmissionId = ref(null);

// Many problems: columns are headed A, B, C … (full title in the tooltip and in the key under the grid).
const lettered = computed(() => props.problems.length >= LETTER_THRESHOLD);
const problemPath = (p) => `/boards/${props.boardSlug}/problems/${p.slug}`;

const cellMap = computed(() => {
  const m = {};
  for (const c of props.cells) m[`${c.userId}:${c.problemId}`] = c;
  return m;
});

function cell(u, p) { return cellMap.value[`${u}:${p}`]; }

// AI hint depth per student+problem. Level 3+ on a problem that is not solved yet reads as "may need help".
const NEEDS_HELP_LEVEL = 3;
const hintMap = computed(() => {
  const m = {};
  for (const h of props.hints) m[`${h.userId}:${h.problemId}`] = h;
  return m;
});
const hint = (u, p) => hintMap.value[`${u}:${p}`];
const stuck = (u, p) => (hint(u, p)?.level ?? 0) >= NEEDS_HELP_LEVEL && !cell(u, p)?.latest;
const needsHelp = (u) => props.problems.some((p) => stuck(u, p.id));

function solvedCount(userId) {
  return props.problems.filter((p) => cell(userId, p.id)?.latest).length;
}

// View-only row order, remembered per board. Clicking a header picks its first order, clicking again flips it.
const SORTS = ['name', 'name-desc', 'solved-desc', 'solved-asc'];
const sortKey = () => `beecoding.board.${props.boardSlug}.gridSort`;
const sort = ref('name');
try { const v = localStorage.getItem(sortKey()); if (SORTS.includes(v)) sort.value = v; } catch { /* ignore */ }

function setSort(by) {
  const [first, second] = by === 'name' ? ['name', 'name-desc'] : ['solved-desc', 'solved-asc'];
  sort.value = sort.value === first ? second : first;
  try { localStorage.setItem(sortKey(), sort.value); } catch { /* ignore */ }
}
const arrow = (by) => (sort.value.startsWith(by) ? (sort.value.endsWith('desc') ? '▼' : '▲') : '');
const ariaSort = (by) => (sort.value.startsWith(by) ? (sort.value.endsWith('desc') ? 'descending' : 'ascending') : 'none');

const byName = (a, b) => a.displayName.localeCompare(b.displayName);
const sortedStudents = computed(() => {
  const dir = sort.value.endsWith('desc') ? -1 : 1;
  const bySolved = sort.value.startsWith('solved');
  return [...props.students].sort((a, b) =>
    (bySolved ? dir * (solvedCount(a.userId) - solvedCount(b.userId)) : dir * byName(a, b)) || byName(a, b));
});
</script>

<template>
  <div class="overflow-x-auto border border-slate-200 dark:border-slate-800 rounded-xl bg-white dark:bg-slate-900">
    <table class="w-full text-sm border-collapse">
      <thead>
        <tr class="bg-slate-50 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
          <th class="text-left font-medium px-3 py-2 sticky left-0 bg-slate-50 dark:bg-slate-800 z-10" :aria-sort="ariaSort('name')">
            <button @click="setSort('name')" class="font-medium hover:text-slate-800 dark:hover:text-slate-100" title="Sort by name">Student <span aria-hidden="true" class="text-[10px]">{{ arrow('name') }}</span></button>
          </th>
          <th v-for="(p, i) in problems" :key="p.id" class="px-2 py-2 font-medium whitespace-nowrap" :class="{ 'text-center': lettered }">
            <RouterLink v-if="boardSlug && p.slug" :to="problemPath(p)"
                        class="hover:text-amber-600 dark:hover:text-amber-400 hover:underline"
                        :title="lettered ? `${letter(i)} · ${p.title}` : `Open “${p.title}”`"
                        :aria-label="lettered ? `${letter(i)}: ${p.title}` : undefined">{{ lettered ? letter(i) : p.title }}</RouterLink>
            <span v-else :title="lettered ? `${letter(i)} · ${p.title}` : undefined">{{ lettered ? letter(i) : p.title }}</span>
          </th>
          <th class="px-3 py-2 font-medium" :aria-sort="ariaSort('solved')">
            <button @click="setSort('solved')" class="font-medium hover:text-slate-800 dark:hover:text-slate-100" title="Sort by number solved">Solved <span aria-hidden="true" class="text-[10px]">{{ arrow('solved') }}</span></button>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="s in sortedStudents" :key="s.userId" class="border-t border-slate-100 dark:border-slate-800">
          <td class="px-3 py-2 sticky left-0 bg-white dark:bg-slate-900 z-10 whitespace-nowrap">
            <span :class="{ 'font-semibold': s.userId === currentUserId }">{{ s.displayName }}</span>
            <span v-if="s.userId === currentUserId" class="text-xs text-slate-400 dark:text-slate-500"> (you)</span>
            <span v-if="isStaff && needsHelp(s.userId)" class="ml-2 text-[10px] px-1.5 py-0.5 rounded border bg-amber-100 text-amber-700 border-amber-200 dark:bg-amber-500/15 dark:text-amber-300 dark:border-amber-500/30"
                  title="Asked the AI tutor for 3+ hints in a row on a problem that is not solved yet">may need help</span>
            <button v-if="isStaff" @click="emit('toggle-hide', s)"
                    class="ml-2 text-[10px] px-1.5 py-0.5 rounded border"
                    :class="s.hiddenByTeacher
                      ? 'bg-purple-100 text-purple-700 border-purple-200 dark:bg-purple-500/15 dark:text-purple-300 dark:border-purple-500/30'
                      : 'text-slate-400 dark:text-slate-500 border-slate-200 dark:border-slate-700 hover:border-slate-400'">
              {{ s.hiddenByTeacher ? 'hidden from peers' : 'visible' }}
            </button>
            <button v-if="isStaff && canRemove" @click="emit('remove', s)" title="Remove from this board"
                    class="ml-1 text-[10px] px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-700 text-slate-400 dark:text-slate-500 hover:text-rose-600 hover:border-rose-300 dark:hover:text-rose-400 dark:hover:border-rose-500/40">
              ✕ remove
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
            <div v-if="isStaff && hint(s.userId, p.id)" class="text-[10px] mt-0.5"
                 :class="stuck(s.userId, p.id) ? 'text-amber-600 dark:text-amber-400 font-semibold' : 'text-slate-400 dark:text-slate-500'"
                 :title="`AI tutor hint level ${hint(s.userId, p.id).level} of 4 (resets after a quiet spell or a new attempt)`">💡{{ hint(s.userId, p.id).level }}</div>
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

    <details v-if="lettered" class="border-t border-slate-100 dark:border-slate-800 px-3 py-2 text-xs text-slate-500 dark:text-slate-400">
      <summary class="cursor-pointer select-none">Column key — {{ problems.length }} problems (hover a letter for its title)</summary>
      <ul class="mt-2 grid gap-x-6 gap-y-1 sm:grid-cols-2 lg:grid-cols-3">
        <li v-for="(p, i) in problems" :key="p.id" class="flex gap-2 min-w-0">
          <span class="font-semibold text-slate-600 dark:text-slate-300 w-7 shrink-0">{{ letter(i) }}</span>
          <RouterLink v-if="boardSlug && p.slug" :to="problemPath(p)" class="truncate hover:underline hover:text-amber-600 dark:hover:text-amber-400">{{ p.title }}</RouterLink>
          <span v-else class="truncate">{{ p.title }}</span>
        </li>
      </ul>
    </details>

    <SubmissionView v-if="viewSubmissionId" :submission-id="viewSubmissionId" @close="viewSubmissionId = null" />
  </div>
</template>
