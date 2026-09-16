<script setup>
import { ref, reactive, onMounted, onBeforeUnmount, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import { useAuth } from '../stores/auth';
import { createBoardConnection } from '../lib/signalr';
import { langLabel } from '../lib/templates';
import { useUndoToast } from '../stores/undoToast';
import ProgressGrid from '../components/ProgressGrid.vue';
import PadletWall from '../components/PadletWall.vue';
import BankPicker from '../components/BankPicker.vue';
import LevelBadge from '../components/LevelBadge.vue';
import VerdictBadge from '../components/VerdictBadge.vue';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';
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

const picking = ref(false);
const stats = ref(null);
const statsOpen = ref(false);
const ENGAGEMENT_GRANULARITY_KEY = 'beecoding.board.stats.engagementGranularity';
const AI_GRANULARITY_KEY = 'beecoding.board.stats.aiGranularity';
const engagementGranularity = ref(localStorage.getItem(ENGAGEMENT_GRANULARITY_KEY) || 'week');   // 'hour' | 'day' | 'week'
const engagementStats = ref(null);
const aiGranularity = ref(localStorage.getItem(AI_GRANULARITY_KEY) || 'week');                    // 'day' | 'week'
const aiEngagementStats = ref(null);

function periodsFor(granularity) {
  return granularity === 'hour' ? 48 : granularity === 'day' ? 14 : 12;
}
async function toggleStats() {
  statsOpen.value = !statsOpen.value;
  if (statsOpen.value && !stats.value) {
    try {
      stats.value = await api.get(`/api/boards/${props.slug}/stats`);
      await Promise.all([loadEngagement(), loadAiEngagement()]);
    } catch (e) { error.value = e.message; }
  }
}
async function loadEngagement() {
  try {
    engagementStats.value = await api.get(
      `/api/boards/${props.slug}/stats/engagement?granularity=${engagementGranularity.value}&periods=${periodsFor(engagementGranularity.value)}`);
  } catch (e) { error.value = e.message; }
}
function setEngagementGranularity(g) {
  engagementGranularity.value = g;
  try { localStorage.setItem(ENGAGEMENT_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadEngagement();
}
async function loadAiEngagement() {
  try {
    aiEngagementStats.value = await api.get(
      `/api/boards/${props.slug}/stats/ai-engagement?granularity=${aiGranularity.value}&periods=${periodsFor(aiGranularity.value)}`);
  } catch (e) { error.value = e.message; }
}
function setAiGranularity(g) {
  aiGranularity.value = g;
  try { localStorage.setItem(AI_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadAiEngagement();
}
const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const shortHour = (s) => new Date(s).toLocaleTimeString(undefined, { hour: 'numeric' });
const engagementLabel = (s) => (engagementGranularity.value === 'hour' ? shortHour(s) : shortDate(s));
const activeUserPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.activeUsers }));
const submissionPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.submissions }));
const aiCallPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.calls }));
const aiTokenPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.totalTokens }));
const topicBarItems = () => (stats.value?.topics || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));


async function saveToBank(p) {
  try {
    await api.post(`/api/boards/${props.slug}/problems/${p.id}/to-bank`);
    error.value = '';
    undoToast.show(`"${p.title}" saved to the problem bank.`);
  } catch (e) { error.value = e.message; }
}

async function toggleHidden(p) {
  try {
    const updated = await api.patch(`/api/boards/${props.slug}/problems/${p.slug}/hidden`, { hidden: !p.hidden });
    p.hidden = updated.hidden;
  } catch (e) { error.value = e.message; }
}

async function onBankAdded() { picking.value = false; await loadAll(); }

async function deleteBoard() {
  const title = board.value.title;
  const slug = props.slug;
  try {
    await api.del(`/api/boards/${slug}`);
    router.push('/boards');
    undoToast.show(`"${title}" deleted.`, () => api.post(`/api/boards/${slug}/restore`));
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
      <h1 class="text-xl font-bold">{{ board.title }}</h1>
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
              class="px-3 py-1.5 rounded-lg text-sm font-medium border"
              :class="progress.examMode
                ? 'bg-purple-600 text-white border-purple-600'
                : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700'">
        {{ progress.examMode ? '🔒 Exam mode ON — peers hidden' : 'Exam mode off' }}
      </button>
      <button @click="toggleLecturing"
              class="px-3 py-1.5 rounded-lg text-sm font-medium border"
              :class="board.lecturingMode
                ? 'bg-sky-600 text-white border-sky-600'
                : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700'">
        {{ board.lecturingMode ? '👨‍🏫 Lecturing ON — students see your code' : 'Lecturing mode' }}
      </button>
      <RouterLink :to="`/boards/${board.slug}/live`"
                  class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
        🎥 Live code
      </RouterLink>
      <button @click="router.push(`/boards/${props.slug}/problems/new`)" class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 text-white">
        + Add problem
      </button>
      <button @click="picking = true"
              class="px-3 py-1.5 rounded-lg text-sm font-medium border bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700">
        📚 From bank
      </button>
      <button @click="toggleStats"
              class="px-3 py-1.5 rounded-lg text-sm font-medium border"
              :class="statsOpen
                ? 'bg-slate-800 text-white border-slate-800 dark:bg-slate-200 dark:text-slate-900 dark:border-slate-200'
                : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-300 dark:border-slate-700'">
        📊 Statistics
      </button>
      <button v-if="board.isOwner" @click="deleteBoard"
              class="sm:ml-auto px-3 py-1.5 rounded-lg text-sm font-medium border border-rose-300 dark:border-rose-500/40 text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-500/10">
        🗑️ Delete board
      </button>
    </div>

    <!-- Staff: per-student visibility (feature 5, per student) -->
    <div v-if="isStaff && progress.students.length" class="flex flex-wrap gap-1.5 mb-4">
      <span class="text-xs text-slate-400 dark:text-slate-500 self-center mr-1">Hide from peers:</span>
      <button v-for="s in progress.students" :key="s.userId" @click="toggleHide(s)"
              class="text-xs px-2 py-0.5 rounded-full border"
              :class="s.hiddenByTeacher
                ? 'bg-purple-100 text-purple-700 border-purple-200 dark:bg-purple-500/15 dark:text-purple-300 dark:border-purple-500/30'
                : 'text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:border-slate-400'">
        {{ s.displayName }} {{ s.hiddenByTeacher ? '🔒' : '' }}
      </button>
    </div>

    <!-- Staff: board statistics (this board only) -->
    <div v-if="isStaff && statsOpen" class="mb-4 space-y-3">
      <div v-if="stats" class="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="text-xs text-slate-400 dark:text-slate-500">Students</div>
          <div class="text-xl font-bold">{{ stats.totalStudents }}</div>
        </div>
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="text-xs text-slate-400 dark:text-slate-500">Problems</div>
          <div class="text-xl font-bold">{{ stats.totalProblems }}</div>
        </div>
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3 col-span-2 sm:col-span-1">
          <div class="text-xs text-slate-400 dark:text-slate-500">Submissions</div>
          <div class="text-xl font-bold">{{ stats.totalSubmissions }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500">
            {{ stats.totalSubmissions ? Math.round(100 * stats.acceptedSubmissions / stats.totalSubmissions) : 0 }}% accepted
          </div>
        </div>
      </div>
      <p v-else class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">Engagement</h2>
          <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="g in [['hour', 'Hourly'], ['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                    @click="setEngagementGranularity(g[0])" class="px-2.5 py-1"
                    :class="engagementGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ g[1] }}
            </button>
          </span>
        </div>
        <div v-if="engagementStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="`Active students / ${engagementGranularity}`" :points="activeUserPoints()" />
          <MiniLineChart :title="`Submissions / ${engagementGranularity}`" :points="submissionPoints()" />
        </div>
        <p v-else-if="engagementStats" class="text-slate-400 dark:text-slate-500 text-sm">No activity in this window yet.</p>
      </div>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">AI usage</h2>
          <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="g in [['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                    @click="setAiGranularity(g[0])" class="px-2.5 py-1"
                    :class="aiGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ g[1] }}
            </button>
          </span>
        </div>
        <div v-if="aiEngagementStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="`AI calls / ${aiGranularity}`" :points="aiCallPoints()" />
          <MiniLineChart :title="`AI tokens / ${aiGranularity}`" :points="aiTokenPoints()" />
        </div>
        <p v-else-if="aiEngagementStats" class="text-slate-400 dark:text-slate-500 text-sm">No AI usage in this window yet.</p>
      </div>

      <div v-if="stats?.topics?.length">
        <h2 class="font-semibold text-sm mb-1.5">Top topics by attempts</h2>
        <TopicBarChart :items="topicBarItems()" />
      </div>
    </div>

    <!-- Student: exam-mode notice -->
    <div v-else-if="progress.examMode" class="my-4 text-sm bg-purple-50 text-purple-700 dark:bg-purple-500/10 dark:text-purple-300 rounded-lg px-3 py-2">
      🔒 Exam mode is on — you can’t see other students’ progress.
    </div>

    <!-- Student: live-coding session running -->
    <RouterLink v-if="!isStaff && board.lecturingMode" :to="`/boards/${board.slug}/live`"
                class="my-4 flex items-center gap-2 text-sm bg-sky-50 text-sky-700 dark:bg-sky-500/10 dark:text-sky-300 rounded-lg px-3 py-2 hover:bg-sky-100 dark:hover:bg-sky-500/15">
      🎥 The teacher is live-coding now — open the shared editor →
    </RouterLink>

    <!-- Problem list -->
    <div class="grid gap-2 my-4">
      <div v-for="p in problems" :key="p.id"
           class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-4 py-3 flex items-center justify-between">
        <div>
          <div class="font-medium flex items-center gap-2 flex-wrap">
            {{ p.title }}
            <LevelBadge :level="p.level" />
            <span v-if="p.hidden" class="text-[10px] px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300">🙈 hidden</span>
            <span v-for="t in (p.tags ? p.tags.split(',') : [])" :key="t"
                  class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
          </div>
          <div class="text-xs text-slate-400 dark:text-slate-500">{{ langLabel(p.allowedLanguages) }} · {{ p.timeLimitMs }}ms · {{ p.memoryLimitKb }}KB</div>
        </div>
        <div class="flex items-center gap-2">
          <button v-if="isStaff" @click="toggleHidden(p)"
                  class="text-sm text-slate-400 dark:text-slate-500 hover:text-slate-900 dark:hover:text-slate-100"
                  :title="p.hidden ? 'Unhide from students' : 'Hide from students'">{{ p.hidden ? '🙈' : '👁️' }}</button>
          <button v-if="isStaff" @click="saveToBank(p)"
                  class="text-sm text-slate-400 dark:text-slate-500 hover:text-slate-900 dark:hover:text-slate-100"
                  title="Save to problem bank">📚</button>
          <button v-if="isStaff" @click="router.push(`/boards/${props.slug}/problems/${p.slug}/edit`)" class="text-sm text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100">edit</button>
          <RouterLink :to="`/boards/${board.slug}/problems/${p.slug}`"
                      class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-3 py-1.5">
            {{ isStaff ? 'View' : 'Solve' }}
          </RouterLink>
        </div>
      </div>
      <p v-if="!problems.length" class="text-slate-400 dark:text-slate-500 text-sm">No problems yet.</p>
    </div>

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


    <BankPicker v-if="picking"
      :board-slug="board.slug"
      @added="onBankAdded" @cancel="picking = false" />

    <SubmissionView v-if="ltiSubmissionId" :submission-id="ltiSubmissionId" @close="ltiSubmissionId = null" />
  </div>
</template>
