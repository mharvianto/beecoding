<script setup>
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { langLabel } from '../lib/templates';
import LevelBadge from '../components/LevelBadge.vue';

const props = defineProps({ slug: { type: String, required: true } });
const router = useRouter();

const board = ref(null);
const problems = ref([]);
const studentCount = ref(0);
const solvedCounts = ref({});   // problemId -> number of students who've solved it
const error = ref('');
const q = ref('');

const isStaff = computed(() => board.value && board.value.role !== 'Student');

async function loadAll() {
  board.value = await api.get(`/api/boards/${props.slug}`);
  problems.value = await api.get(`/api/boards/${props.slug}/problems`);

  if (isStaff.value) {
    const progress = await api.get(`/api/boards/${props.slug}/progress`);
    studentCount.value = progress.students.length;
    const counts = {};
    for (const c of progress.cells) if (c.latest) counts[c.problemId] = (counts[c.problemId] || 0) + 1;
    solvedCounts.value = counts;
  }
}

onMounted(async () => {
  try { await loadAll(); } catch (e) { error.value = e.message; }
});

const filtered = computed(() => {
  const needle = q.value.trim().toLowerCase();
  if (!needle) return problems.value;
  return problems.value.filter((p) =>
    p.title.toLowerCase().includes(needle) ||
    (p.tags || '').toLowerCase().includes(needle));
});

async function toggleHidden(p) {
  try {
    const updated = await api.patch(`/api/boards/${props.slug}/problems/${p.slug}/hidden`, { hidden: !p.hidden });
    p.hidden = updated.hidden;
  } catch (e) { error.value = e.message; }
}

// ---- drag-and-drop reorder (staff only, disabled while a search filter is active) ----
const canReorder = computed(() => isStaff.value && !q.value);
const dragSlug = ref(null);
function onDragStart(p) { dragSlug.value = p.slug; }
function onDragOver(e) { e.preventDefault(); }
async function onDrop(target) {
  if (!dragSlug.value || dragSlug.value === target.slug) return;
  const list = problems.value;
  const from = list.findIndex((p) => p.slug === dragSlug.value);
  const to = list.findIndex((p) => p.slug === target.slug);
  if (from === -1 || to === -1) return;
  const [moved] = list.splice(from, 1);
  list.splice(to, 0, moved);
  dragSlug.value = null;
  try {
    await api.patch(`/api/boards/${props.slug}/problems/reorder`, { order: list.map((p) => p.slug) });
  } catch (e) { error.value = e.message; }
}
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-6" v-if="board">
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to {{ board.title }}</RouterLink>
    <div class="flex items-center justify-between mt-2 mb-4">
      <h1 class="text-xl font-bold">Problems</h1>
      <button v-if="isStaff" @click="router.push(`/boards/${slug}/problems/new`)" class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 text-white">
        + Add problem
      </button>
    </div>
    <p v-if="error" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ error }}</p>

    <input v-model="q" type="search" placeholder="Search by title or tag..."
           class="w-full text-sm border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 mb-3" />
    <p v-if="q && filtered.length !== problems.length" class="text-xs text-slate-400 dark:text-slate-500 mb-2">
      {{ filtered.length }} of {{ problems.length }} problems
    </p>
    <p v-if="canReorder && problems.length > 1" class="text-xs text-slate-400 dark:text-slate-500 mb-2">
      Drag ⠿ to reorder
    </p>

    <div class="grid gap-2">
      <div v-for="p in filtered" :key="p.id"
           :draggable="canReorder"
           @dragstart="onDragStart(p)" @dragover="onDragOver" @drop="onDrop(p)"
           class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-4 py-3 flex items-center justify-between"
           :class="{ 'opacity-50': dragSlug === p.slug }">
        <div class="flex items-start gap-2 min-w-0">
          <span v-if="canReorder" class="cursor-grab text-slate-300 dark:text-slate-600 select-none mt-0.5" title="Drag to reorder">⠿</span>
          <div class="min-w-0">
            <div class="font-medium flex items-center gap-2 flex-wrap">
              {{ p.title }}
              <LevelBadge :level="p.level" />
              <span v-if="p.hidden" class="text-[10px] px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300">🙈 hidden</span>
              <span v-for="t in (p.tags ? p.tags.split(',') : [])" :key="t"
                    class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
            </div>
            <div class="text-xs text-slate-400 dark:text-slate-500">
              {{ langLabel(p.allowedLanguages) }} · {{ p.timeLimitMs }}ms · {{ p.memoryLimitKb }}KB
              <span v-if="isStaff"> · ✅ {{ solvedCounts[p.id] || 0 }}/{{ studentCount }} solved</span>
            </div>
          </div>
        </div>
        <div class="flex items-center gap-1 shrink-0">
          <button v-if="isStaff" @click="toggleHidden(p)" :title="p.hidden ? 'Unhide from students' : 'Hide from students'" class="row-action-btn">
            <span>{{ p.hidden ? '🙈' : '👁️' }}</span><span>{{ p.hidden ? 'Unhide' : 'Hide' }}</span>
          </button>
          <button v-if="isStaff" @click="router.push(`/boards/${slug}/problems/${p.slug}/edit`)" class="row-action-btn">
            <span>✏️</span><span>Edit</span>
          </button>
          <RouterLink :to="`/boards/${slug}/problems/${p.slug}`" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-3 py-1.5 ml-1">
            {{ isStaff ? 'View' : 'Solve' }}
          </RouterLink>
        </div>
      </div>
      <p v-if="!problems.length" class="text-slate-400 dark:text-slate-500 text-sm">No problems yet.</p>
      <p v-else-if="!filtered.length" class="text-slate-400 dark:text-slate-500 text-sm">No problems match "{{ q }}".</p>
    </div>
  </div>
</template>
