<script setup>
import { ref, reactive, onMounted, onBeforeUnmount, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import { useAuth } from '../stores/auth';
import { createBoardConnection } from '../lib/signalr';
import { useUndoToast } from '../stores/undoToast';
import ProgressGrid from '../components/ProgressGrid.vue';
import PadletWall from '../components/PadletWall.vue';
import QrCode from '../components/QrCode.vue';
import SubmissionView from '../components/SubmissionView.vue';

const props = defineProps({ slug: { type: String, required: true } });
const auth = useAuth();
const route = useRoute();
const router = useRouter();
const undoToast = useUndoToast();

// Deep link from an LTI "review submission" launch (?viewSubmission=<id>, see LtiController)
// — open it once on load, then drop the query param so a refresh doesn't reopen it.
const ltiSubmissionId = ref(route.query.viewSubmission ? Number(route.query.viewSubmission) : null);
if (ltiSubmissionId.value) {
  const q = { ...route.query };
  delete q.viewSubmission;
  router.replace({ query: q });
}

const view = ref(localStorage.getItem('beecoding.boardView') || 'wall');
function setView(v) { view.value = v; localStorage.setItem('beecoding.boardView', v); }
function openCard({ problemSlug }) { router.push(`/boards/${props.slug}/problems/${problemSlug}`); }

const board = ref(null);
const problems = ref([]);
const progress = ref({ students: [], problems: [], cells: [], examMode: false, viewerIsStaff: false });
const presence = ref([]);
const error = ref('');
let conn = null;
let refreshTimer = null;

const isStaff = computed(() => board.value && board.value.role !== 'Student');
const showQr = ref(false);
const joinUrl = computed(() =>
  board.value ? `${window.location.origin}${withBase('/join/' + board.value.joinCode)}` : '');

async function loadAll() {
  board.value = await api.get(`/api/boards/${props.slug}`);
  problems.value = await api.get(`/api/boards/${props.slug}/problems`);
  await loadProgress();
}
async function loadProgress() {
  progress.value = await api.get(`/api/boards/${props.slug}/progress`);
}
const wallSignal = ref(0);
const drafts = reactive({});   // "problemId:userId" -> { code, updatedAt, authorName }
function scheduleRefresh() {
  clearTimeout(refreshTimer);
  refreshTimer = setTimeout(loadProgress, 250);
  wallSignal.value++;
}

async function toggleExam() {
  board.value = await api.patch(`/api/boards/${props.slug}`, { examMode: !progress.value.examMode });
  await loadProgress();
}
async function toggleLecturing() {
  board.value = await api.patch(`/api/boards/${props.slug}`, { lecturingMode: !board.value.lecturingMode });
}
async function toggleHide(student) {
  await api.patch(`/api/boards/${props.slug}/members/${student.userId}`, { hiddenByTeacher: !student.hiddenByTeacher });
  await loadProgress();
}

const tagList = computed(() => (board.value?.tags ? board.value.tags.split(',') : []));
const editingTags = ref(false);
const tagsInput = ref('');
function startEditTags() { tagsInput.value = board.value.tags || ''; editingTags.value = true; }
async function saveTags() {
  try {
    board.value = await api.patch(`/api/boards/${props.slug}`, { tags: tagsInput.value });
    editingTags.value = false;
  } catch (e) { error.value = e.message; }
}

const settingsOpen = ref(false);

async function deleteBoard() {
  const title = board.value.title;
  const slug = props.slug;
  try {
    await api.del(`/api/boards/${slug}`);
    router.push('/boards');
    undoToast.show(`"${title}" deleted.`, async () => {
      await api.post(`/api/boards/${slug}/restore`);
      // Undo can fire well after we've already navigated to the boards list (a different
      // component instance whose own list we have no handle on) — a full reload is the
      // simplest way to guarantee the restored board actually reappears.
      window.location.reload();
    });
  } catch (e) { error.value = e.message; }
}

// Authoritative live-draft snapshot (server filters by visibility for students).
async function refreshDrafts() {
  if (!conn || conn.state !== 'Connected') return;
  try {
    const list = await conn.invoke('GetDrafts', board.value.id);
    for (const k of Object.keys(drafts)) delete drafts[k];
    for (const d of list || [])
      drafts[`${d.problemId}:${d.userId}`] = { code: d.code, updatedAt: d.updatedAt, authorName: d.authorName };
  } catch { /* ignore */ }
}

onMounted(async () => {
  try { await loadAll(); } catch (e) { error.value = e.message; return; }

  conn = createBoardConnection();
  conn.on('progressChanged', scheduleRefresh);
  conn.on('wallChanged', () => { wallSignal.value++; refreshDrafts(); });
  conn.on('memberVisibilityChanged', () => { scheduleRefresh(); refreshDrafts(); });
  conn.on('examModeChanged', async () => { await loadAll(); refreshDrafts(); });
  conn.on('boardSettingsChanged', async () => { await loadAll(); });
  conn.on('problemChanged', async () => { problems.value = await api.get(`/api/boards/${props.slug}/problems`); scheduleRefresh(); });
  conn.on('presence', (list) => { presence.value = list; });
  conn.on('draftUpdated', (d) => {
    drafts[`${d.problemId}:${d.userId}`] = { code: d.code, updatedAt: d.updatedAt, authorName: d.authorName };
  });
  try {
    await conn.start();
    await conn.invoke('JoinBoard', board.value.id);
    await refreshDrafts();
  } catch (e) { /* realtime is best-effort */ }
});

onBeforeUnmount(async () => {
  clearTimeout(refreshTimer);
  try { await conn?.invoke('LeaveBoard', board.value?.id); } catch {}
  await conn?.stop();
});
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-6" v-if="board">
    <div class="flex items-center justify-between mb-1">
      <div>
        <h1 class="text-xl font-bold">{{ board.title }}</h1>
        <div v-if="!editingTags" class="flex flex-wrap items-center gap-1 mt-1">
          <span v-for="t in tagList" :key="t"
                class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
          <button v-if="board.isOwner || auth.user?.isAdmin" @click="startEditTags" class="row-action-btn">
            {{ tagList.length ? '✏️ Edit tags' : '+ Add tags' }}
          </button>
        </div>
        <div v-else class="flex items-center gap-2 mt-1">
          <input v-model="tagsInput" placeholder="e.g. class-2026, semester-1" @keyup.enter="saveTags"
                 class="text-xs border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 w-64" />
          <button @click="saveTags" class="row-action-btn row-action-btn--success">Save</button>
          <button @click="editingTags = false" class="row-action-btn">Cancel</button>
        </div>
      </div>
      <div class="text-sm text-slate-500 dark:text-slate-400 flex items-center gap-3">
        <span v-if="presence.length">🟢 {{ presence.length }} online</span>
        <span v-if="isStaff" class="flex items-center gap-2">
          Join code: <span class="font-mono font-semibold text-slate-700 dark:text-slate-200">{{ board.joinCode }}</span>
          <button @click="showQr = !showQr"
                  class="text-xs px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-700 hover:border-amber-400 dark:hover:border-amber-500">
            {{ showQr ? 'Hide QR' : 'Show QR' }}
          </button>
        </span>
      </div>
    </div>
    <p v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</p>

    <div v-if="isStaff && showQr" class="flex flex-col items-center gap-2 my-4">
      <QrCode :text="joinUrl" />
      <p class="text-xs text-slate-400 dark:text-slate-500">
        Scan to join instantly — or enter code <span class="font-mono font-semibold text-slate-600 dark:text-slate-300">{{ board.joinCode }}</span> manually
      </p>
    </div>

    <!-- Staff controls -->
    <div v-if="isStaff" class="flex flex-wrap items-center gap-2 my-4">
      <button @click="toggleExam"
              class="px-4 py-2 rounded-lg text-sm font-semibold border-2"
              :class="progress.examMode
                ? 'bg-purple-600 text-white border-purple-600 shadow-md shadow-purple-500/30'
                : 'bg-white dark:bg-slate-900 text-purple-700 dark:text-purple-300 border-purple-300 dark:border-purple-500/40 hover:border-purple-500'">
        {{ progress.examMode ? '🔒 Exam mode ON — peers hidden' : '🔓 Exam mode off' }}
      </button>
      <RouterLink :to="`/boards/${board.slug}/stats`"
                  class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
        📊 Statistics
      </RouterLink>
      <RouterLink :to="`/boards/${board.slug}/submissions`"
                  class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
        🧾 Submissions
      </RouterLink>

      <div class="relative sm:ml-auto">
        <button @click="settingsOpen = !settingsOpen"
                class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
          ⚙️ Settings
        </button>
        <div v-if="settingsOpen" class="fixed inset-0 z-40" @click="settingsOpen = false"></div>
        <div v-if="settingsOpen"
             class="absolute right-0 z-50 mt-1 w-60 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-lg shadow-lg py-1">
          <button @click="toggleLecturing(); settingsOpen = false"
                  class="w-full text-left px-3 py-2 text-sm hover:bg-slate-50 dark:hover:bg-slate-800 flex items-center gap-2"
                  :class="board.lecturingMode ? 'text-sky-600 dark:text-sky-400 font-medium' : 'text-slate-600 dark:text-slate-300'">
            👨‍🏫 {{ board.lecturingMode ? 'Lecturing ON — students see your code' : 'Lecturing mode' }}
          </button>
          <RouterLink :to="`/boards/${board.slug}/live`" @click="settingsOpen = false"
                      class="block px-3 py-2 text-sm text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800">
            🎥 Live code
          </RouterLink>
          <RouterLink :to="`/boards/${board.slug}/plagiarism`" @click="settingsOpen = false"
                      class="block px-3 py-2 text-sm text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800">
            🔍 Plagiarism check
          </RouterLink>
          <template v-if="board.isOwner">
            <div class="border-t border-slate-100 dark:border-slate-800 my-1"></div>
            <button @click="deleteBoard"
                    class="w-full text-left px-3 py-2 text-sm text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-500/10">
              🗑️ Delete board
            </button>
          </template>
        </div>
      </div>
    </div>

    <!-- Student: exam-mode notice -->
    <div v-if="progress.examMode" class="my-4 text-sm bg-purple-50 text-purple-700 dark:bg-purple-500/10 dark:text-purple-300 rounded-lg px-3 py-2">
      🔒 Exam mode is on — you can’t see other students’ progress.
    </div>

    <!-- Student: live-coding session running -->
    <RouterLink v-if="!isStaff && board.lecturingMode" :to="`/boards/${board.slug}/live`"
                class="my-4 flex items-center gap-2 text-sm bg-sky-50 text-sky-700 dark:bg-sky-500/10 dark:text-sky-300 rounded-lg px-3 py-2 hover:bg-sky-100 dark:hover:bg-sky-500/15">
      🎥 The teacher is live-coding now — open the shared editor →
    </RouterLink>

    <!-- Problems -->
    <RouterLink :to="`/boards/${board.slug}/problems`"
                class="my-4 flex items-center justify-between bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-4 py-3 hover:border-amber-400 dark:hover:border-amber-500">
      <span class="text-sm font-medium">📋 Problems ({{ problems.length }})</span>
      <span class="text-slate-400 dark:text-slate-500 text-sm">{{ isStaff ? 'View & manage' : 'Browse & solve' }} →</span>
    </RouterLink>

    <!-- Live board -->
    <div class="flex items-center justify-between mt-8 mb-2">
      <h2 class="font-semibold text-slate-600 dark:text-slate-300 text-sm">Live progress</h2>
      <div class="flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
        <button @click="setView('wall')" class="px-3 py-1"
                :class="view === 'wall' ? 'bg-amber-500 text-white' : 'bg-white dark:bg-slate-900 text-slate-500 dark:text-slate-400'">Wall</button>
        <button @click="setView('grid')" class="px-3 py-1 border-l border-slate-300 dark:border-slate-700"
                :class="view === 'grid' ? 'bg-amber-500 text-white' : 'bg-white dark:bg-slate-900 text-slate-500 dark:text-slate-400'">Grid</button>
      </div>
    </div>

    <PadletWall v-if="view === 'wall'"
      :board-slug="board.slug"
      :current-user-id="auth.user?.id"
      :refresh-signal="wallSignal"
      :drafts="drafts" />

    <ProgressGrid v-else
      :students="progress.students"
      :problems="progress.problems"
      :cells="progress.cells"
      :is-staff="progress.viewerIsStaff"
      :current-user-id="auth.user?.id"
      @toggle-hide="toggleHide" />

    <SubmissionView v-if="ltiSubmissionId" :submission-id="ltiSubmissionId" @close="ltiSubmissionId = null" />
  </div>
</template>
