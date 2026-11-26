<script setup>
import { tr } from '../lib/i18n';
import Chevron from '../components/Chevron.vue';
import Icon from '../components/Icon.vue';
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { langLabel } from '../lib/templates';
import LevelBadge from '../components/LevelBadge.vue';
import BankPicker from '../components/BankPicker.vue';
import GroupEditor from '../components/GroupEditor.vue';
import ExportMenu from '../components/ExportMenu.vue';
import MarkdownBlock from '../components/MarkdownBlock.vue';
import BoardContinue from '../components/BoardContinue.vue';
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

// Copies behind the bank problem they came from (owner only): slug -> locked (students already submitted).
const bankUpdates = ref({});
async function loadBankUpdates() {
  try {
    const rows = await api.get(`/api/boards/${props.slug}/problems/bank-updates`);
    bankUpdates.value = Object.fromEntries(rows.map((r) => [r.slug, r.locked]));
  } catch { bankUpdates.value = {}; }
}
async function updateFromBank(p) {
  const locked = bankUpdates.value[p.slug];
  const ok = await confirmDialog.ask(locked
    ? tr('Students have already submitted to “{p0}”, so it is left as it is. The bank\'s current version is added as a new hidden draft next to it — publish it, then hide this one.', { p0: p.title })
    : tr('Replace “{p0}” with the bank\'s current statement, tests and limits? Any edits you made to this copy are lost.', { p0: p.title }),
  { confirmLabel: locked ? tr('Create draft') : tr('Update') });
  if (!ok) return;
  error.value = '';
  try {
    await api.post(`/api/boards/${props.slug}/problems/${p.slug}/update-from-bank`);
    await Promise.all([loadAll(), loadBankUpdates()]);
  } catch (e) { error.value = e.message; }
}

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

// open "this problem is wrong" reports per problem (staff only)
const openReports = ref({});
async function loadOpenReports() {
  try { openReports.value = (await api.get('/api/problem-reports/open-counts')).board || {}; } catch { /* no badges */ }
}

// Problems that board members proposed from their own bank (owner only). Approving copies one onto
// the board as a hidden problem, so nothing reaches students until the owner unhides it.
const proposals = ref([]);
const openProposal = ref(null);     // id whose statement/tests are expanded
const rejecting = ref(null);        // { id, reason }
const proposalBusy = ref(false);
const pendingProposals = computed(() => proposals.value.filter((p) => p.status === 'Pending'));
const answeredProposals = computed(() => proposals.value.filter((p) => p.status !== 'Pending'));

async function loadProposals() {
  try { proposals.value = await api.get(`/api/boards/${props.slug}/proposals`); } catch { /* panel stays empty */ }
}

async function approveProposal(p) {
  error.value = ''; proposalBusy.value = true;
  try {
    await api.post(`/api/boards/${props.slug}/proposals/${p.id}/approve`);
    await Promise.all([loadProposals(), loadAll()]);
  } catch (e) { error.value = e.message; }
  finally { proposalBusy.value = false; }
}

async function rejectProposal() {
  const r = rejecting.value;
  error.value = ''; proposalBusy.value = true;
  try {
    await api.post(`/api/boards/${props.slug}/proposals/${r.id}/reject`, { reason: r.reason.trim() || null });
    rejecting.value = null;
    await loadProposals();
  } catch (e) { error.value = e.message; }
  finally { proposalBusy.value = false; }
}

onMounted(async () => {
  try { await loadAll(); } catch (e) { error.value = e.message; }
  if (isStaff.value) loadOpenReports();
  if (board.value?.isOwner) { loadProposals(); loadBankUpdates(); }
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
  const section = (g) => ({ group: g, items: list.filter((p) => p.groupId === g.id) });
  const out = groups.value.filter((g) => !g.archived).map(section);
  const known = new Set(groups.value.map((g) => g.id));
  const loose = list.filter((p) => p.groupId == null || !known.has(p.groupId));
  if (loose.length || !groups.value.length) out.push({ group: null, items: loose });
  out.push(...groups.value.filter((g) => g.archived).reverse().map(section));   // the archive comes last, newest first
  // While searching, drop sections with no hits so results aren't buried in empty headers.
  return q.value.trim() ? out.filter((sec) => sec.items.length) : out;
});
const firstArchivedId = computed(() => sections.value.find((s) => s.group?.archived)?.group.id);
const collapsed = ref(loadCollapsed());
function loadCollapsed() {
  try { return JSON.parse(localStorage.getItem(`beecoding.groups.collapsed.${props.slug}`) || '{}'); } catch { return {}; }
}
// Until the person folds or unfolds a section themselves, finished ones (archived or closed) are folded (but not while searching).
const isCollapsed = (sec) => collapsed.value[secKey(sec.group)]
  ?? (!!(sec.group?.archived || sec.group?.state === 'Closed') && !q.value.trim());
function toggleCollapsed(sec) {
  const key = secKey(sec.group);
  collapsed.value = { ...collapsed.value, [key]: !isCollapsed(sec) };
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
// One click. Archiving also unhides: the archive is for students to look back at.
async function setArchived(g, archived) {
  try {
    await api.put(`/api/boards/${props.slug}/groups/${g.id}`, {
      title: g.title, hidden: archived ? false : g.hidden, examMode: g.examMode, opensAt: utc(g.opensAt), closesAt: utc(g.closesAt), archived,
    });
    await loadAll();
  } catch (e) { error.value = e.message; }
}
async function deleteGroup(g) {
  const ok = await confirmDialog.ask(tr('Delete the group "{p0}"? Its problems are kept and become ungrouped.', { p0: g.title }), { confirmLabel: tr('Delete group') });
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
const utc = (iso) => (!iso || iso.endsWith('Z') ? iso : iso + 'Z');   // the server's UTC times may come without the suffix
const fmtWhen = (iso) => new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' });
function stateLabel(g) {
  if (g.state === 'Upcoming') return tr('⏳ Opens {p0}', { p0: fmtWhen(g.opensAt) });
  if (g.state === 'Archived') return tr('Archived · still open for submissions');
  if (g.state === 'Closed') return tr('🔚 Closed {p0}', { p0: fmtWhen(g.closesAt) });
  return g.closesAt ? tr('🟢 Open · closes {p0}', { p0: fmtWhen(g.closesAt) }) : g.opensAt ? tr('🟢 Open since {p0}', { p0: fmtWhen(g.opensAt) }) : '';
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
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-500 dark:text-slate-400">← {{ tr('back to {title}', { title: board.title }) }}</RouterLink>
    <div class="flex items-center justify-between gap-2 flex-wrap mt-2 mb-4">
      <h1 class="text-xl font-bold">{{ tr('Problems') }}</h1>
      <div v-if="isStaff" class="flex items-center gap-2 flex-wrap">
        <button @click="editingGroup = null"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
          <Icon name="folder" /> {{ tr('Add group') }}
        </button>
        <button @click="picking = true"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
          <Icon name="book" /> {{ tr('From bank') }}
        </button>
        <button @click="router.push(`/boards/${slug}/problems/new`)" class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 text-slate-900">
          {{ tr('+ Add problem') }}
        </button>
      </div>
    </div>
    <p v-if="error" role="alert" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ tr(error) }}</p>
    <BoardContinue v-if="!isStaff" :board-slug="slug" />

    <section v-if="board?.isOwner && proposals.length"
             class="mb-4 border border-amber-200 dark:border-amber-500/30 rounded-xl p-3 bg-amber-50/40 dark:bg-amber-500/5">
      <h2 class="text-sm font-semibold mb-2"><Icon name="bulb" /> {{ tr('Proposed problems') }}
        <span v-if="pendingProposals.length" class="ml-1 text-[11px] px-1.5 py-0.5 rounded-full bg-amber-500 text-slate-900">{{ pendingProposals.length }}</span>
      </h2>
      <p class="text-[11px] text-slate-500 dark:text-slate-400 mb-2">
        {{ tr('Approving adds the problem to this board') }} <b>{{ tr('hidden') }}</b>, with all of its tests — unhide it when you want students to see it.
      </p>
      <ul class="space-y-2">
        <li v-for="p in proposals" :key="p.id"
            class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-lg p-2">
          <div class="flex items-start justify-between gap-2 flex-wrap">
            <div class="min-w-0">
              <button @click="openProposal = openProposal === p.id ? null : p.id" class="font-medium text-sm text-left hover:underline">{{ p.title }}</button>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1 flex-wrap">
                <LevelBadge :level="p.level" /> by {{ p.proposerName }} · {{ p.testCases.length }} tests ({{ p.testCases.filter(t => t.isSample).length }} visible, {{ p.testCases.filter(t => !t.isSample).length }} hidden)
                <span v-if="p.status !== 'Pending'" class="px-1.5 py-0.5 rounded-full"
                      :class="p.status === 'Approved' ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'">
                  {{ p.status === 'Approved' ? tr('approved') : tr('declined') }}
                </span>
              </div>
              <div v-if="p.inputFileName || p.bannedHeaders || p.bannedSymbols" class="mt-1.5 flex flex-wrap gap-1.5 text-[11px]">
              <span v-if="p.inputFileName" class="rounded px-2 py-0.5 bg-sky-50 dark:bg-sky-500/10 text-sky-700 dark:text-sky-300"><Icon name="file" /> {{ tr('reads input from file') }} <span class="font-mono">{{ p.inputFileName }}</span>, not stdin</span>
              <span v-if="p.bannedHeaders" class="rounded px-2 py-0.5 bg-amber-50 dark:bg-amber-500/10 text-amber-700 dark:text-amber-300"><Icon name="ban" /> {{ tr('headers:') }} <span class="font-mono">{{ p.bannedHeaders }}</span></span>
              <span v-if="p.bannedSymbols" class="rounded px-2 py-0.5 bg-amber-50 dark:bg-amber-500/10 text-amber-700 dark:text-amber-300"><Icon name="ban" /> {{ tr('functions:') }} <span class="font-mono">{{ p.bannedSymbols }}</span></span>
            </div>
              <p v-if="p.note" class="text-xs text-slate-500 dark:text-slate-400 mt-1">“{{ p.note }}”</p>
              <p v-if="p.reviewNote" class="text-xs text-rose-600 dark:text-rose-400 mt-1">{{ tr('Declined:') }} {{ p.reviewNote }}</p>
            </div>
            <div v-if="p.status === 'Pending'" class="flex items-center gap-1 shrink-0">
              <button @click="approveProposal(p)" :disabled="proposalBusy" class="row-action-btn row-action-btn--success"><span><Icon name="check" /></span><span>{{ tr('Approve') }}</span></button>
              <button @click="rejecting = { id: p.id, reason: '' }" :disabled="proposalBusy" class="row-action-btn row-action-btn--danger"><span><Icon name="x" /></span><span>{{ tr('Decline') }}</span></button>
            </div>
          </div>
          <div v-if="rejecting && rejecting.id === p.id" class="mt-2 flex gap-2 flex-wrap">
            <input v-model="rejecting.reason" maxlength="300" :placeholder="tr('Reason (optional, the student sees it)')" :aria-label="tr('Reason (optional, the student sees it)')"
                   class="flex-1 min-w-48 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" />
            <button @click="rejectProposal" :disabled="proposalBusy" class="bg-rose-600 text-white rounded-lg px-3 py-1 text-sm">{{ tr('Decline') }}</button>
            <button @click="rejecting = null" class="text-slate-500 dark:text-slate-400 text-sm px-2">{{ tr('Cancel') }}</button>
          </div>
          <div v-if="openProposal === p.id" class="mt-2 space-y-2 border-t border-slate-200 dark:border-slate-800 pt-2">
            <MarkdownBlock :text="p.statementMarkdown" />
            <details class="text-xs">
              <summary class="cursor-pointer text-slate-500 dark:text-slate-400">{{ tr('Tests ({n})', { n: p.testCases.length }) }}</summary>
              <template v-for="(t, i) in p.testCases" :key="t.id ?? i">
              <div class="mt-2 flex items-center gap-1.5 flex-wrap">#{{ i + 1 }}
                <span v-if="t.isSample" class="px-1.5 py-0.5 rounded bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300"><Icon name="eye" /> {{ tr('visible to students (sample)') }}</span>
                <span v-else class="px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300"><Icon name="lock" /> {{ tr('hidden from students (graded)') }}</span>
              </div>
              <div class="mt-1 grid grid-cols-2 gap-2">
                <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1 overflow-auto max-h-24">{{ t.stdin }}</pre>
                <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1 overflow-auto max-h-24">{{ t.expectedStdout }}</pre>
              </div>
              </template>
            </details>
          </div>
        </li>
      </ul>
    </section>

    <input v-model="q" type="search" :placeholder="tr('Search by title or tag...')" :aria-label="tr('Search by title or tag...')"
           class="w-full text-sm border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 mb-3" />
    <p v-if="q && filtered.length !== problems.length" class="text-xs text-slate-500 dark:text-slate-400 mb-2">
      {{ tr('{a} of {b} problems', { a: filtered.length, b: problems.length }) }}
    </p>
    <p v-if="canReorder && problems.length > 1" class="max-sm:hidden text-xs text-slate-500 dark:text-slate-400 mb-2">
      {{ tr('Drag ⠿ to reorder or move problems between groups — drop on a group\'s header to send a problem to its end. Groups can be dragged by their ⠿ too.') }}
    </p>

    <div class="space-y-3">
      <section v-for="sec in sections" :key="sec.group ? sec.group.id : 'none'"
               @dragover="overSection($event, sec)" @drop="dropOnSection($event, sec)"
               class="transition-shadow"
               :class="{ 'shadow-[0_-4px_0_0_rgb(245_158_11)]': isOverGroup(sec, 'before'), 'shadow-[0_4px_0_0_rgb(245_158_11)]': isOverGroup(sec, 'after') }">
        <h2 v-if="sec.group && sec.group.id === firstArchivedId" class="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400 border-t border-slate-200 dark:border-slate-800 pt-3 mb-2">
          <Icon name="folder" /> {{ tr('Archive') }}
        </h2>
        <!-- section header: only when the board actually uses groups (also the drop target for "send to this group") -->
        <div v-if="groups.length" class="flex items-center gap-2 flex-wrap mb-2 rounded-lg transition-colors"
             :class="isOverSection(sec) ? 'bg-amber-50 dark:bg-amber-500/10 ring-1 ring-amber-300 dark:ring-amber-500/40' : ''"
             :draggable="canReorder && !!sec.group"
             @dragstart="sec.group && dragStart($event, { kind: 'group', id: sec.group.id })" @dragend="dragEnd">
          <span v-if="canReorder && sec.group" class="max-sm:hidden cursor-grab text-slate-300 dark:text-slate-600 select-none" :title="tr('Drag to reorder groups')">⠿</span>
          <button @click="toggleCollapsed(sec)" class="text-slate-500 dark:text-slate-400 text-left"
                  :aria-expanded="!isCollapsed(sec)"
                  :title="isCollapsed(sec) ? 'Expand' : 'Collapse'">
            <Chevron :open="!isCollapsed(sec)" />
          </button>
          <h2 class="font-semibold text-sm text-slate-700 dark:text-slate-200">{{ sec.group ? sec.group.title : tr('Ungrouped') }}</h2>
          <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr(sec.items.length === 1 ? '{n} problem' : '{n} problems', { n: sec.items.length }) }}</span>
          <ExportMenu v-if="isStaff && !sec.group && sec.items.length" class="ml-auto" :slug="slug" :group="0" label="Download ungrouped problems" @error="error = $event" />
          <template v-if="sec.group">
            <span v-if="sec.group.hidden" class="text-[11px] px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300"><Icon name="eyeoff" /> {{ tr('draft') }}</span>
            <span v-for="l in [sec.group.examMode && 'peers hidden', sec.group.blockPaste && 'no paste', sec.group.noAiHints && 'no AI hints'].filter(Boolean)" :key="l"
                  class="text-[11px] px-1.5 py-0.5 rounded bg-purple-100 text-purple-700 dark:bg-purple-500/15 dark:text-purple-300"><Icon name="lock" /> {{ tr(l) }}</span>
            <span v-if="stateLabel(sec.group)" class="text-[11px] px-1.5 py-0.5 rounded"
                  :class="sec.group.state === 'Upcoming' ? 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300'
                    : sec.group.state === 'Closed' ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
                    : 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'">{{ stateLabel(sec.group) }}</span>
            <span v-if="isStaff" class="sm:ml-auto flex items-center gap-1 max-sm:w-full max-sm:pl-6">
              <ExportMenu :slug="slug" :group="sec.group.id" :label="`Download ${sec.group.title}`" @error="error = $event" />
              <button @click="moveGroup(sec.group, -1)" class="row-action-btn" :title="tr('Move group up')"><span>↑</span></button>
              <button @click="moveGroup(sec.group, 1)" class="row-action-btn" :title="tr('Move group down')"><span>↓</span></button>
              <button @click="setArchived(sec.group, !sec.group.archived)" class="row-action-btn">{{ sec.group.archived ? tr('Unarchive') : tr('Archive') }}</button>
              <button @click="editingGroup = sec.group" class="row-action-btn" :aria-label="tr('Edit')"><span><Icon name="pencil" /></span><span class="max-sm:hidden">{{ tr('Edit') }}</span></button>
              <button @click="deleteGroup(sec.group)" class="row-action-btn row-action-btn--danger" :aria-label="tr('Delete')"><span><Icon name="trash" /></span><span class="max-sm:hidden">{{ tr('Delete') }}</span></button>
            </span>
          </template>
        </div>

        <div v-show="!isCollapsed(sec)" class="grid gap-2">
          <div v-for="p in sec.items" :key="p.id"
               :draggable="canReorder"
               @dragstart="dragStart($event, { kind: 'problem', slug: p.slug })" @dragend="dragEnd"
               @dragover="overProblem($event, p)" @drop="dropOnProblem($event, p)"
               class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-3 sm:px-4 py-3 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 sm:gap-3"
               :class="{
                 'opacity-50': drag?.kind === 'problem' && drag.slug === p.slug,
                 'shadow-[0_-3px_0_0_rgb(245_158_11)]': isOverProblem(p, 'before'),
                 'shadow-[0_3px_0_0_rgb(245_158_11)]': isOverProblem(p, 'after'),
               }">
            <div class="flex items-start gap-2 min-w-0">
              <span v-if="canReorder" class="max-sm:hidden cursor-grab text-slate-300 dark:text-slate-600 select-none mt-0.5" :title="tr('Drag to reorder')">⠿</span>
              <div class="min-w-0">
                <div class="font-medium flex items-center gap-2 flex-wrap">
                  {{ p.title }}
                  <LevelBadge :level="p.level" />
                  <span v-if="p.hidden" class="text-[11px] px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300"><Icon name="eyeoff" /> {{ tr('hidden') }}</span>
                  <RouterLink v-if="isStaff && openReports[p.id]" :to="`/reports/problems?problem=${p.id}`"
                              class="text-[11px] px-1.5 py-0.5 rounded bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300 hover:underline"
                              :title="`${openReports[p.id]} open report(s) — click to review`"><Icon name="flag" /> {{ openReports[p.id] }}</RouterLink>
                </div>
                <div v-if="p.tags" class="flex flex-wrap gap-1 mt-1">
                  <span v-for="t in p.tags.split(',')" :key="t"
                        class="text-[11px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400">{{ t.trim() }}</span>
                </div>
                <div class="text-xs text-slate-500 dark:text-slate-400 mt-1 flex flex-wrap gap-x-3 gap-y-0.5">
                  <span class="whitespace-nowrap">{{ langLabel(p.allowedLanguages) }}</span>
                  <span class="whitespace-nowrap">{{ p.timeLimitMs }}ms</span>
                  <span class="whitespace-nowrap">{{ p.memoryLimitKb }}KB</span>
                  <template v-if="isStaff">
                    <span class="whitespace-nowrap"><Icon name="check" /> {{ tr('{a}/{b} solved', { a: stats[p.id]?.solvedCount || 0, b: studentCount }) }}</span>
                    <span class="whitespace-nowrap">{{ tr('{n} submissions', { n: stats[p.id]?.submissions || 0 }) }}</span>
                    <span class="whitespace-nowrap">{{ Math.round((stats[p.id]?.acceptRate || 0) * 100) }}% AC</span>
                  </template>
                </div>
              </div>
            </div>
            <div class="flex items-center gap-1 flex-wrap sm:flex-nowrap sm:shrink-0">
              <select v-if="isStaff && groups.length" :value="p.groupId ?? ''" @change="moveProblem(p, $event.target.value === '' ? null : Number($event.target.value))"
                  :title="tr('Move to group')" class="text-xs border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-1.5 py-1 max-w-[9rem] min-w-0" :aria-label="tr('Ungrouped')">
            <option value="">{{ tr('Ungrouped') }}</option>
            <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.title }}</option>
          </select>
          <button v-if="bankUpdates[p.slug] !== undefined" @click="updateFromBank(p)" class="row-action-btn row-action-btn--accent"
                  :title="tr('The bank problem this was copied from has changed since')" :aria-label="tr('Update from bank')">
                <span><Icon name="refresh" /></span><span class="max-sm:hidden">{{ tr('Update from bank') }}</span>
              </button>
          <button v-if="isStaff" @click="toggleHidden(p)" :title="p.hidden ? 'Unhide from students' : 'Hide from students'" class="row-action-btn" :aria-label="p.hidden ? tr('Unhide') : tr('Hide')">
                <span><Icon :name="p.hidden ? 'eyeoff' : 'eye'" /></span><span class="max-sm:hidden">{{ p.hidden ? tr('Unhide') : tr('Hide') }}</span>
              </button>
              <button v-if="isStaff" @click="router.push({ path: `/boards/${slug}/plagiarism`, query: { problem: p.slug } })" class="row-action-btn"
                      :title="tr('Compare students\' latest submissions to this problem')" :aria-label="tr('Similarity')">
                <span><Icon name="search" /></span><span class="max-sm:hidden">{{ tr('Similarity') }}</span>
              </button>
              <button v-if="isStaff" @click="router.push(`/boards/${slug}/problems/${p.slug}/edit`)" class="row-action-btn" :aria-label="tr('Edit')">
                <span><Icon name="pencil" /></span><span class="max-sm:hidden">{{ tr('Edit') }}</span>
              </button>
              <ExportMenu v-if="isStaff" :slug="slug" :problem="p.slug" label="Download results and answers for this problem" @error="error = $event" />
              <RouterLink :to="`/boards/${slug}/problems/${p.slug}`" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 ml-auto sm:ml-1">
                {{ isStaff ? tr('View') : tr('Solve') }}
              </RouterLink>
            </div>
          </div>
          <p v-if="!sec.items.length && !q && groups.length" class="text-slate-500 dark:text-slate-400 text-sm px-1">
            {{ sec.group ? (canReorder ? tr('No problems in this group yet — drag a problem here, or use its group menu.') : tr('No problems in this group yet.')) : tr('Every problem is in a group.') }}
          </p>
        </div>
      </section>
      <p v-if="!problems.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No problems yet.') }}</p>
      <p v-else-if="!filtered.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No problems match "{q}".', { q }) }}</p>
    </div>

    <GroupEditor v-if="editingGroup !== undefined" :group="editingGroup" @save="saveGroup" @cancel="editingGroup = undefined" />
    <BankPicker v-if="picking" :board-slug="slug" @added="onBankAdded" @cancel="picking = false" />
  </div>
</template>
