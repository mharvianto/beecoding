<script setup>
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { langLabel } from '../lib/templates';
import LevelBadge from '../components/LevelBadge.vue';
import BankPicker from '../components/BankPicker.vue';
import GroupEditor from '../components/GroupEditor.vue';
import { useConfirmDialog } from '../stores/confirmDialog';

const props = defineProps({ slug: { type: String, required: true } });
const router = useRouter();
const confirmDialog = useConfirmDialog();

const board = ref(null);
const problems = ref([]);
const groups = ref([]);
const studentCount = ref(0);
const stats = ref({});   // problemId -> { submissions, accepted, acceptRate, solvedCount }
const error = ref('');
const q = ref('');
const picking = ref(false);

const isStaff = computed(() => board.value && board.value.role !== 'Student');

async function loadAll() {
  board.value = await api.get(`/api/boards/${props.slug}`);
  [problems.value, groups.value] = await Promise.all([
    api.get(`/api/boards/${props.slug}/problems`),
    api.get(`/api/boards/${props.slug}/groups`),
  ]);

  if (isStaff.value) {
    const [progress, problemStats] = await Promise.all([
      api.get(`/api/boards/${props.slug}/progress`),
      api.get(`/api/boards/${props.slug}/problems/stats`),
    ]);
    studentCount.value = progress.students.length;
    stats.value = Object.fromEntries(problemStats.map((s) => [s.problemId, s]));
  }
}

async function onBankAdded() { picking.value = false; await loadAll(); }

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

// ---- groups (one-level folders/sessions) ----
// Each group is a section; problems with no group form the trailing "Ungrouped" section.
const sections = computed(() => {
  const list = filtered.value;
  const out = groups.value.map((g) => ({ group: g, items: list.filter((p) => p.groupId === g.id) }));
  const known = new Set(groups.value.map((g) => g.id));
  const loose = list.filter((p) => p.groupId == null || !known.has(p.groupId));
  if (loose.length || !groups.value.length) out.push({ group: null, items: loose });
  // While searching, drop sections with no hits so results aren't buried in empty headers.
  return q.value.trim() ? out.filter((sec) => sec.items.length) : out;
});
const collapsed = ref(loadCollapsed());
function loadCollapsed() {
  try { return JSON.parse(localStorage.getItem(`beecoding.groups.collapsed.${props.slug}`) || '{}'); } catch { return {}; }
}
function toggleCollapsed(key) {
  collapsed.value = { ...collapsed.value, [key]: !collapsed.value[key] };
  try { localStorage.setItem(`beecoding.groups.collapsed.${props.slug}`, JSON.stringify(collapsed.value)); } catch { /* ignore */ }
}

const editingGroup = ref(undefined);   // undefined = closed, null = new, object = editing
async function saveGroup(payload) {
  try {
    if (editingGroup.value) await api.put(`/api/boards/${props.slug}/groups/${editingGroup.value.id}`, payload);
    else await api.post(`/api/boards/${props.slug}/groups`, payload);
    editingGroup.value = undefined;
    await loadAll();
  } catch (e) { error.value = e.message; }
}
async function deleteGroup(g) {
  const ok = await confirmDialog.ask(`Delete the group "${g.title}"? Its problems are kept and become ungrouped.`, { confirmLabel: 'Delete group' });
  if (!ok) return;
  try { await api.del(`/api/boards/${props.slug}/groups/${g.id}`); await loadAll(); } catch (e) { error.value = e.message; }
}
async function moveGroup(g, delta) {
  const ids = groups.value.map((x) => x.id);
  const i = ids.indexOf(g.id);
  const j = i + delta;
  if (j < 0 || j >= ids.length) return;
  [ids[i], ids[j]] = [ids[j], ids[i]];
  try { await api.patch(`/api/boards/${props.slug}/groups/reorder`, { order: ids }); await loadAll(); } catch (e) { error.value = e.message; }
}
async function moveProblem(p, groupId) {
  try {
    const updated = await api.patch(`/api/boards/${props.slug}/problems/${p.slug}/group`, { groupId });
    p.groupId = updated.groupId;
    groups.value = await api.get(`/api/boards/${props.slug}/groups`);
  } catch (e) { error.value = e.message; }
}
const fmtWhen = (iso) => new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' });
function stateLabel(g) {
  if (g.state === 'Upcoming') return `⏳ Opens ${fmtWhen(g.opensAt)}`;
  if (g.state === 'Closed') return `🔚 Closed ${fmtWhen(g.closesAt)}`;
  return g.closesAt ? `🟢 Open · closes ${fmtWhen(g.closesAt)}` : g.opensAt ? `🟢 Open since ${fmtWhen(g.opensAt)}` : '';
}

async function toggleHidden(p) {
  try {
    const updated = await api.patch(`/api/boards/${props.slug}/problems/${p.slug}/hidden`, { hidden: !p.hidden });
    p.hidden = updated.hidden;
  } catch (e) { error.value = e.message; }
}

// ---- drag-and-drop (staff only, disabled while a search filter is active) ----
// A problem can be dropped on another problem (before/after it, joining that problem's group),
// or on a group header / empty group area (end of that group). A group can be dropped on
// another group to reorder. `drag` is what is being dragged; `over` drives the drop indicator.
const canReorder = computed(() => isStaff.value && !q.value);
const drag = ref(null);   // { kind: 'problem', slug } | { kind: 'group', id }
const over = ref(null);   // { kind: 'problem', slug, pos } | { kind: 'section', key } | { kind: 'group', key, pos }
const secKey = (g) => (g ? g.id : 'none');
const isOverProblem = (p, pos) => over.value?.kind === 'problem' && over.value.slug === p.slug && over.value.pos === pos;
const isOverSection = (sec) => over.value?.kind === 'section' && over.value.key === secKey(sec.group);
const isOverGroup = (sec, pos) => over.value?.kind === 'group' && over.value.key === secKey(sec.group) && over.value.pos === pos;

function dragStart(e, payload) {
  drag.value = payload;
  e.dataTransfer.effectAllowed = 'move';
  e.dataTransfer.setData('text/plain', payload.kind === 'problem' ? payload.slug : String(payload.id));   // Firefox needs data
}
function dragEnd() { drag.value = null; over.value = null; }
const halfway = (e) => {
  const r = e.currentTarget.getBoundingClientRect();
  return e.clientY < r.top + r.height / 2 ? 'before' : 'after';
};

function overProblem(e, p) {
  if (drag.value?.kind !== 'problem') return;
  e.stopPropagation();
  if (drag.value.slug === p.slug) { over.value = null; return; }   // not a drop target for itself
  e.preventDefault();
  e.dataTransfer.dropEffect = 'move';
  over.value = { kind: 'problem', slug: p.slug, pos: halfway(e) };
}
async function dropOnProblem(e, p) {
  if (drag.value?.kind !== 'problem') return;
  e.preventDefault(); e.stopPropagation();
  const pos = halfway(e);
  const slug = drag.value.slug;
  dragEnd();
  if (slug !== p.slug) await moveProblemTo(slug, p.groupId ?? null, { target: p.slug, pos });
}

function overSection(e, sec) {
  if (!drag.value) return;
  if (drag.value.kind === 'problem') {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    over.value = { kind: 'section', key: secKey(sec.group) };
  } else if (sec.group?.id !== drag.value.id) {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    over.value = { kind: 'group', key: secKey(sec.group), pos: sec.group ? halfway(e) : 'after' };
  }
}
async function dropOnSection(e, sec) {
  if (!drag.value) return;
  e.preventDefault();
  const d = drag.value;
  const pos = over.value?.kind === 'group' ? over.value.pos : 'after';
  dragEnd();
  if (d.kind === 'problem') await moveProblemTo(d.slug, sec.group?.id ?? null, null);
  else if (sec.group?.id !== d.id) await reorderGroupTo(d.id, sec.group, pos);
}

/** Move a problem into `groupId`, before/after `anchor.target`, or (no anchor) to the end of that group. */
async function moveProblemTo(slug, groupId, anchor) {
  const list = [...problems.value];
  const from = list.findIndex((x) => x.slug === slug);
  if (from === -1) return;
  const [moved] = list.splice(from, 1);
  const groupChanged = (moved.groupId ?? null) !== groupId;
  let to;
  if (anchor) {
    to = list.findIndex((x) => x.slug === anchor.target) + (anchor.pos === 'after' ? 1 : 0);
  } else {
    let last = -1;
    list.forEach((x, i) => { if ((x.groupId ?? null) === groupId) last = i; });
    to = last < 0 ? list.length : last + 1;
  }
  list.splice(to, 0, moved);
  moved.groupId = groupId;
  problems.value = list;   // optimistic; the calls below persist it
  try {
    if (groupChanged) await api.patch(`/api/boards/${props.slug}/problems/${slug}/group`, { groupId });
    await api.patch(`/api/boards/${props.slug}/problems/reorder`, { order: list.map((x) => x.slug) });
    if (groupChanged) groups.value = await api.get(`/api/boards/${props.slug}/groups`);
  } catch (e) { error.value = e.message; await loadAll(); }
}

async function reorderGroupTo(id, target, pos) {
  const ids = groups.value.map((g) => g.id).filter((x) => x !== id);
  const to = target ? ids.indexOf(target.id) + (pos === 'after' ? 1 : 0) : ids.length;
  ids.splice(to, 0, id);
  const byId = new Map(groups.value.map((g) => [g.id, g]));
  groups.value = ids.map((x) => byId.get(x));   // optimistic
  try { await api.patch(`/api/boards/${props.slug}/groups/reorder`, { order: ids }); }
  catch (e) { error.value = e.message; await loadAll(); }
}
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-6" v-if="board">
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to {{ board.title }}</RouterLink>
    <div class="flex items-center justify-between mt-2 mb-4">
      <h1 class="text-xl font-bold">Problems</h1>
      <div v-if="isStaff" class="flex items-center gap-2">
        <button @click="editingGroup = null"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
          📁 Add group
        </button>
        <button @click="picking = true"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
          📚 From bank
        </button>
        <button @click="router.push(`/boards/${slug}/problems/new`)" class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 text-white">
          + Add problem
        </button>
      </div>
    </div>
    <p v-if="error" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ error }}</p>

    <input v-model="q" type="search" placeholder="Search by title or tag..."
           class="w-full text-sm border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 mb-3" />
    <p v-if="q && filtered.length !== problems.length" class="text-xs text-slate-400 dark:text-slate-500 mb-2">
      {{ filtered.length }} of {{ problems.length }} problems
    </p>
    <p v-if="canReorder && problems.length > 1" class="text-xs text-slate-400 dark:text-slate-500 mb-2">
      Drag ⠿ to reorder or move problems between groups — drop on a group's header to send a problem to its end. Groups can be dragged by their ⠿ too.
    </p>

    <div class="space-y-5">
      <section v-for="sec in sections" :key="sec.group ? sec.group.id : 'none'"
               @dragover="overSection($event, sec)" @drop="dropOnSection($event, sec)"
               class="transition-shadow"
               :class="{ 'shadow-[0_-4px_0_0_rgb(245_158_11)]': isOverGroup(sec, 'before'), 'shadow-[0_4px_0_0_rgb(245_158_11)]': isOverGroup(sec, 'after') }">
        <!-- section header: only when the board actually uses groups (also the drop target for "send to this group") -->
        <div v-if="groups.length" class="flex items-center gap-2 flex-wrap mb-2 rounded-lg transition-colors"
             :class="isOverSection(sec) ? 'bg-amber-50 dark:bg-amber-500/10 ring-1 ring-amber-300 dark:ring-amber-500/40' : ''"
             :draggable="canReorder && !!sec.group"
             @dragstart="sec.group && dragStart($event, { kind: 'group', id: sec.group.id })" @dragend="dragEnd">
          <span v-if="canReorder && sec.group" class="cursor-grab text-slate-300 dark:text-slate-600 select-none" title="Drag to reorder groups">⠿</span>
          <button @click="toggleCollapsed(sec.group ? sec.group.id : 'none')" class="text-slate-400 dark:text-slate-500 w-4 text-left"
                  :title="collapsed[sec.group ? sec.group.id : 'none'] ? 'Expand' : 'Collapse'">
            {{ collapsed[sec.group ? sec.group.id : 'none'] ? '▸' : '▾' }}
          </button>
          <h2 class="font-semibold text-sm text-slate-700 dark:text-slate-200">{{ sec.group ? sec.group.title : 'Ungrouped' }}</h2>
          <span class="text-xs text-slate-400 dark:text-slate-500">{{ sec.items.length }} problem{{ sec.items.length === 1 ? '' : 's' }}</span>
          <template v-if="sec.group">
            <span v-if="sec.group.hidden" class="text-[10px] px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300">🙈 hidden</span>
            <span v-if="sec.group.examMode" class="text-[10px] px-1.5 py-0.5 rounded bg-purple-100 text-purple-700 dark:bg-purple-500/15 dark:text-purple-300">🔒 exam</span>
            <span v-if="stateLabel(sec.group)" class="text-[10px] px-1.5 py-0.5 rounded"
                  :class="sec.group.state === 'Upcoming' ? 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300'
                    : sec.group.state === 'Closed' ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
                    : 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'">{{ stateLabel(sec.group) }}</span>
            <span v-if="isStaff" class="ml-auto flex items-center gap-1">
              <button @click="moveGroup(sec.group, -1)" class="row-action-btn" title="Move group up"><span>↑</span></button>
              <button @click="moveGroup(sec.group, 1)" class="row-action-btn" title="Move group down"><span>↓</span></button>
              <button @click="editingGroup = sec.group" class="row-action-btn"><span>✏️</span><span>Edit</span></button>
              <button @click="deleteGroup(sec.group)" class="row-action-btn row-action-btn--danger"><span>🗑️</span><span>Delete</span></button>
            </span>
          </template>
        </div>

        <div v-show="!collapsed[sec.group ? sec.group.id : 'none']" class="grid gap-2">
          <div v-for="p in sec.items" :key="p.id"
               :draggable="canReorder"
               @dragstart="dragStart($event, { kind: 'problem', slug: p.slug })" @dragend="dragEnd"
               @dragover="overProblem($event, p)" @drop="dropOnProblem($event, p)"
               class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-4 py-3 flex items-center justify-between"
               :class="{
                 'opacity-50': drag?.kind === 'problem' && drag.slug === p.slug,
                 'shadow-[0_-3px_0_0_rgb(245_158_11)]': isOverProblem(p, 'before'),
                 'shadow-[0_3px_0_0_rgb(245_158_11)]': isOverProblem(p, 'after'),
               }">
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
                  <template v-if="isStaff">
                    · ✅ {{ stats[p.id]?.solvedCount || 0 }}/{{ studentCount }} solved
                    · {{ stats[p.id]?.submissions || 0 }} submissions
                    · {{ Math.round((stats[p.id]?.acceptRate || 0) * 100) }}% AC
                  </template>
                </div>
              </div>
            </div>
            <div class="flex items-center gap-1 shrink-0">
              <select v-if="isStaff && groups.length" :value="p.groupId ?? ''" @change="moveProblem(p, $event.target.value === '' ? null : Number($event.target.value))"
                  title="Move to group" class="text-xs border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-1.5 py-1 max-w-[9rem]">
            <option value="">Ungrouped</option>
            <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.title }}</option>
          </select>
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
          <p v-if="!sec.items.length && !q && groups.length" class="text-slate-400 dark:text-slate-500 text-sm px-1">
            {{ sec.group ? (canReorder ? 'No problems in this group yet — drag a problem here, or use its group menu.' : 'No problems in this group yet.') : 'Every problem is in a group.' }}
          </p>
        </div>
      </section>
      <p v-if="!problems.length" class="text-slate-400 dark:text-slate-500 text-sm">No problems yet.</p>
      <p v-else-if="!filtered.length" class="text-slate-400 dark:text-slate-500 text-sm">No problems match "{{ q }}".</p>
    </div>

    <GroupEditor v-if="editingGroup !== undefined" :group="editingGroup" @save="saveGroup" @cancel="editingGroup = undefined" />
    <BankPicker v-if="picking" :board-slug="slug" @added="onBankAdded" @cancel="picking = false" />
  </div>
</template>
