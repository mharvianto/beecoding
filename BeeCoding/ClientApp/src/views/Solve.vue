<script setup>
import { ref, onMounted, onBeforeUnmount, computed, watch } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useProgress } from '../stores/progress';
import { createBoardConnection } from '../lib/signalr';
import MonacoEditor from '../components/MonacoEditor.vue';
import MarkdownBlock from '../components/MarkdownBlock.vue';
import VerdictBadge from '../components/VerdictBadge.vue';
import LevelBadge from '../components/LevelBadge.vue';
import ContentGuard from '../components/ContentGuard.vue';
import StatementImage from '../components/StatementImage.vue';
import AiHint from '../components/AiHint.vue';
import SplitPane from '../components/SplitPane.vue';
import { CODE_TEMPLATES, isPristine, allowedLangs, langLabel } from '../lib/templates';
import { loadDraft, saveDraft, clearDraft } from '../lib/draft';
import { celebrate } from '../lib/confetti';
import { alreadyCelebrated, markCelebrated } from '../lib/celebration';

const props = defineProps({ slug: { type: String, required: true }, problemSlug: { type: String, required: true } });
const auth = useAuth();
const progress = useProgress();

const board = ref(null);
const problem = ref(null);
// internal id of the loaded problem — used for API calls / SignalR that key by int id
const pid = computed(() => problem.value?.id);
const protectOn = computed(() => !!board.value?.protectContent && auth.user?.role === 'Student');
const watermark = computed(() =>
  `${auth.user?.email || auth.user?.displayName || ''} · ${new Date().toLocaleString()}`);
const code = ref('');
const stdin = ref('');
const solveLang = ref('cpp');   // 'c' | 'cpp' — student's choice of compiler

// local autosave so an accidental refresh doesn't wipe the editor
const draftScope = computed(() => `board:${props.problemSlug}`);
const restored = ref(false);
const restoredAt = ref('');
let saveTimer = null;
function saveDraftNow() {
  if (problem.value) saveDraft(auth.user?.id, draftScope.value, code.value, solveLang.value);
}
function useTemplate() {
  code.value = CODE_TEMPLATES[solveLang.value] || '';
  clearDraft(auth.user?.id, draftScope.value);
  restored.value = false;
}

// languages this problem accepts (empty allow-list on the problem => all of them)
const langs = computed(() => allowedLangs(problem.value?.allowedLanguages));
const langNote = computed(() =>
  problem.value?.allowedLanguages ? langLabel(problem.value.allowedLanguages) : '');

function setLang(l) {
  if (l === solveLang.value || !langs.value.includes(l)) return;
  solveLang.value = l;
  // swap the boilerplate only if the student hasn't written their own code
  if (isPristine(code.value)) code.value = CODE_TEMPLATES[l] || '';
  try { localStorage.setItem('beecoding.lang', l); } catch { /* ignore */ }
}
const runOut = ref(null);
const running = ref(false);
const submitting = ref(false);
const submittingId = ref(null);
const testProgress = ref(null);   // { current, total } | null
const submissions = ref([]);
const error = ref('');
const myPost = ref({ postId: null, hiddenByStudent: false });
let conn = null;

const mine = computed(() => submissions.value.filter((s) => s.mine));
const latestMine = computed(() => mine.value[0]);

// lecturing mode
const isStaff = computed(() => board.value?.role === 'Owner' || board.value?.role === 'Teacher');
const lecture = ref(null);            // { code, language, teacherName, updatedAt } — what the teacher is typing (seen by students)
const showLecture = ref(true);
const studentDrafts = ref({});        // userId -> { authorName, code, updatedAt } — for the teacher monitor
const openStudent = ref(null);
const showMonitor = ref(true);
const studentList = computed(() =>
  Object.entries(studentDrafts.value)
    .map(([uid, d]) => ({ uid, ...d }))
    .sort((a, b) => (b.updatedAt || '').localeCompare(a.updatedAt || '')));

let lectureTimer = null;
function pushLectureSoon() {
  if (!conn || conn.state !== 'Connected' || !isStaff.value || !board.value?.lecturingMode) return;
  clearTimeout(lectureTimer);
  lectureTimer = setTimeout(() => {
    conn.invoke('PushLecture', board.value.id, pid.value, code.value, solveLang.value).catch(() => {});
  }, 700);
}
function pushLectureNow() {
  if (conn?.state === 'Connected' && isStaff.value && board.value?.lecturingMode)
    conn.invoke('PushLecture', board.value.id, pid.value, code.value, solveLang.value).catch(() => {});
}
function useLectureCode() {
  if (!lecture.value) return;
  code.value = lecture.value.code;
  if (lecture.value.language === 'c' || lecture.value.language === 'cpp') setLang(lecture.value.language);
}

async function load() {
  board.value = await api.get(`/api/boards/${props.slug}`);
  problem.value = await api.get(`/api/boards/${props.slug}/problems/${props.problemSlug}`);
  let pref = null;
  try { pref = localStorage.getItem('beecoding.lang'); } catch { /* ignore */ }
  const allowed = allowedLangs(problem.value.allowedLanguages);
  solveLang.value = allowed.includes(pref) ? pref : allowed[0];
  code.value = CODE_TEMPLATES[solveLang.value] || '';

  // bring back an unsaved draft from a previous visit / refresh
  const d = loadDraft(auth.user?.id, draftScope.value);
  if (d && d.code.trim() && !isPristine(d.code)) {
    code.value = d.code;
    if (d.lang === 'c' || d.lang === 'cpp') solveLang.value = d.lang;
    restored.value = true;
    restoredAt.value = new Date(d.ts).toLocaleString();
  }

  if (problem.value.sampleTests?.[0]) stdin.value = problem.value.sampleTests[0].stdin;
  await loadSubs();
  // Resume tracking a still-grading submission across a refresh/reopen — otherwise the
  // progress bar only ever shows up for the exact submit() call that started it.
  const myLatest = submissions.value.find((s) => s.mine);
  if (myLatest && myLatest.status !== 'Done') submittingId.value = myLatest.id;
}
async function loadSubs() {
  submissions.value = await api.get(`/api/problems/${pid.value}/submissions`);
  // Self-heal if a submissionResult push was ever missed (e.g. a dropped connection) —
  // otherwise "Still checking…" could get stuck showing after grading actually finished.
  if (submittingId.value) {
    const match = submissions.value.find((s) => s.id === submittingId.value);
    if (match && match.status === 'Done') { submittingId.value = null; testProgress.value = null; }
  }
  // Celebrate a genuine first-time solve as soon as we see it — whether that's via a live
  // push or discovered here on reload/reopen after grading finished while unwatched (e.g.
  // the tab was closed mid-grading). xpAwarded is only >0 the one time a problem is newly
  // solved; the localStorage marker stops a later revisit from re-celebrating it.
  const mineLatest = submissions.value.find((s) => s.mine);
  if (mineLatest?.status === 'Done' && mineLatest.verdict === 'Accepted' && mineLatest.xpAwarded > 0
      && !alreadyCelebrated(auth.user?.id, draftScope.value, mineLatest.id)) {
    celebrate();
    markCelebrated(auth.user?.id, draftScope.value, mineLatest.id);
  }
}

async function run() {
  error.value = ''; running.value = true; runOut.value = null;
  try {
    runOut.value = await api.post('/api/run', { language: solveLang.value, code: code.value, stdin: stdin.value, problemId: pid.value });
  } catch (e) { error.value = e.message; }
  finally { running.value = false; }
}

async function submit() {
  error.value = ''; submitting.value = true; testProgress.value = null;
  try {
    const res = await api.post(`/api/problems/${pid.value}/submit`, { code: code.value, language: solveLang.value });
    submittingId.value = res.submissionId;
    await loadSubs();
  } catch (e) { error.value = e.message; }
  finally { submitting.value = false; }
}

async function toggleHiddenFromPeers() {
  const next = !myPost.value.hiddenByStudent;
  await api.patch(`/api/posts/${myPost.value.postId}/visibility`, { hiddenByStudent: next });
  myPost.value.hiddenByStudent = next;
  await loadSubs();
}

let draftTimer = null;
const isStudent = () => auth.user?.role === 'Student';

function pushDraftSoon() {
  if (!conn || conn.state !== 'Connected' || !isStudent()) return;
  clearTimeout(draftTimer);
  draftTimer = setTimeout(() => {
    conn.invoke('PushDraft', board.value.id, pid.value, code.value).catch(() => {});
  }, 900);
}
watch(code, () => {
  pushDraftSoon();
  pushLectureSoon();
  clearTimeout(saveTimer);
  saveTimer = setTimeout(saveDraftNow, 500);
});
watch(solveLang, () => { saveDraftNow(); pushLectureSoon(); });
watch(() => board.value?.lecturingMode, (on) => { if (on) pushLectureNow(); });

onMounted(async () => {
  try { await load(); } catch (e) { error.value = e.message; return; }
  window.addEventListener('beforeunload', saveDraftNow);
  // Make sure a wall card exists for this student even before they run/submit.
  if (isStudent()) {
    try { myPost.value = await api.post(`/api/problems/${pid.value}/post`); } catch { /* ignore */ }
  }

  conn = createBoardConnection();
  conn.on('submissionResult', (dto) => {
    if (dto.problemId === pid.value) { loadSubs(); progress.refresh(); }
    if (dto.id === submittingId.value) { testProgress.value = null; submittingId.value = null; }
  });
  conn.on('submissionProgress', (p) => {
    if (p.kind === 'board' && p.submissionId === submittingId.value) testProgress.value = { current: p.current, total: p.total };
  });
  conn.on('progressBumped', (p) => { progress.$patch({ ...p, ready: true }); });
  conn.on('boardSettingsChanged', async () => {
    try { board.value = await api.get(`/api/boards/${props.slug}`); } catch { /* ignore */ }
  });
  conn.on('lectureUpdated', (l) => {
    if (l.problemId === pid.value && !isStaff.value) lecture.value = l;
  });
  conn.on('draftUpdated', (d) => {
    if (d.problemId === pid.value && isStaff.value && d.userId !== auth.user?.id)
      studentDrafts.value[d.userId] = { authorName: d.authorName, code: d.code, updatedAt: d.updatedAt };
  });
  try {
    await conn.start();
    await conn.invoke('JoinBoard', board.value.id);
    if (isStudent()) conn.invoke('PushDraft', board.value.id, pid.value, code.value).catch(() => {});
    if (isStaff.value) {
      pushLectureNow();
      try {
        const ds = await conn.invoke('GetDrafts', board.value.id);
        (ds || []).filter((d) => d.problemId === pid.value && d.userId !== auth.user?.id)
          .forEach((d) => { studentDrafts.value[d.userId] = { authorName: d.authorName, code: d.code, updatedAt: d.updatedAt }; });
      } catch { /* ignore */ }
    } else {
      try {
        const l = await conn.invoke('GetLecture', board.value.id, pid.value);
        if (l) lecture.value = l;
      } catch { /* ignore */ }
    }
  } catch {}
});
onBeforeUnmount(async () => {
  clearTimeout(draftTimer);
  clearTimeout(saveTimer);
  clearTimeout(lectureTimer);
  saveDraftNow();
  window.removeEventListener('beforeunload', saveDraftNow);
  try { await conn?.stop(); } catch {}
});

function ago(ts) {
  const s = Math.max(0, (Date.now() - new Date(ts + (ts?.endsWith('Z') ? '' : 'Z')).getTime()) / 1000);
  return s < 60 ? `${s | 0}s` : s < 3600 ? `${(s / 60) | 0}m` : `${(s / 3600) | 0}h`;
}
</script>

<template>
  <div v-if="problem" class="h-full">
   <SplitPane direction="horizontal" storage-key="beecoding.split.solve-main" :initial="42" :initial-stacked="34" :min="260">
    <template #a>
    <!-- Left: statement + submissions -->
    <div class="h-full overflow-y-auto p-5 border-r border-slate-200 dark:border-slate-800">
      <RouterLink :to="`/boards/${props.slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to board</RouterLink>
      <div class="flex items-center gap-2 mt-2 mb-1 flex-wrap">
        <h1 class="text-lg font-bold">{{ problem.title }}</h1>
        <LevelBadge :level="problem.level" />
        <span v-for="t in (problem.tags ? problem.tags.split(',') : [])" :key="t"
              class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
      </div>
      <div class="text-xs text-slate-400 dark:text-slate-500 mb-3">
        {{ langNote || (solveLang === 'c' ? 'C' : 'C++') }} · limit {{ problem.timeLimitMs }} ms · {{ problem.memoryLimitKb }} KB
      </div>
      <p v-if="problem.bannedHeaders || problem.bannedSymbols" class="mb-3 text-xs bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-300 rounded-lg px-3 py-2 space-y-0.5">
        <span v-if="problem.bannedHeaders" class="block">🚫 Banned headers: <span class="font-mono">{{ problem.bannedHeaders }}</span> (and <span class="font-mono">bits/stdc++.h</span>).</span>
        <span v-if="problem.bannedSymbols" class="block">🚫 Banned functions: <span class="font-mono">{{ problem.bannedSymbols }}</span>.</span>
        <span class="block">Implement it yourself — a violation fails as a Compile Error, on Run and Submit.</span>
      </p>
      <p v-if="problem.inputFileName" class="mb-3 text-xs bg-sky-50 dark:bg-sky-500/10 text-sky-700 dark:text-sky-300 rounded-lg px-3 py-2">
        📄 This problem reads input from a file named <span class="font-mono">{{ problem.inputFileName }}</span> in the current directory — not from stdin.
      </p>

      <div v-if="restored" class="mb-3 text-xs bg-amber-100 dark:bg-amber-500/15 text-amber-800 dark:text-amber-200 rounded-lg px-3 py-2 flex items-center gap-2 flex-wrap">
        <span>↩︎ Restored your unsaved code from {{ restoredAt }}.</span>
        <button @click="useTemplate" class="underline hover:no-underline">Use the template instead</button>
        <button @click="restored = false" class="ml-auto text-amber-600 dark:text-amber-300" title="Dismiss">✕</button>
      </div>

      <p v-if="protectOn" class="text-[11px] text-amber-600 dark:text-amber-400 mb-2">
        🔒 Protected problem — served as an encrypted image watermarked with your identity.
      </p>

      <ContentGuard v-if="protectOn" :active="true" :watermark="''">
        <StatementImage :problem-id="pid" />
      </ContentGuard>

      <template v-else>
        <MarkdownBlock :text="problem.statementMarkdown" />
        <div v-if="problem.sampleTests?.length" class="mt-4">
          <h3 class="font-semibold text-sm mb-1">Samples</h3>
          <div v-for="(t, i) in problem.sampleTests" :key="i" class="grid grid-cols-2 gap-2 mb-2 text-xs">
            <pre class="bg-slate-100 dark:bg-slate-800 rounded p-2 overflow-x-auto">{{ t.stdin }}</pre>
            <pre class="bg-slate-100 dark:bg-slate-800 rounded p-2 overflow-x-auto">{{ t.expectedStdout }}</pre>
          </div>
        </div>
      </template>

      <button v-if="isStudent() && myPost.postId" @click="toggleHiddenFromPeers"
              class="mt-4 w-full text-sm px-3 py-2 rounded-lg border flex items-center justify-center gap-2"
              :class="myPost.hiddenByStudent
                ? 'bg-purple-100 text-purple-700 border-purple-200 dark:bg-purple-500/15 dark:text-purple-300 dark:border-purple-500/30'
                : 'text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:border-slate-400'">
        {{ myPost.hiddenByStudent
          ? "🔒 Live code & progress hidden from classmates"
          : "👥 Hide live code & progress from classmates" }}
      </button>

      <!-- lecturing mode: teacher's live code, shown to students -->
      <div v-if="board?.lecturingMode && !isStaff && lecture" class="mt-4 border border-sky-200 dark:border-sky-500/30 rounded-xl overflow-hidden">
        <button @click="showLecture = !showLecture"
                class="w-full flex items-center justify-between px-3 py-2 text-sm font-semibold bg-sky-50 dark:bg-sky-500/10 text-sky-700 dark:text-sky-300">
          <span>👨‍🏫 {{ lecture.teacherName }}'s code · live</span>
          <span class="text-xs font-normal">{{ ago(lecture.updatedAt) }} ago · {{ showLecture ? '▾' : '▸' }}</span>
        </button>
        <div v-if="showLecture" class="p-2 space-y-2">
          <pre class="bg-slate-900 text-slate-100 dark:bg-black rounded-lg p-2 font-mono text-xs overflow-auto max-h-72 whitespace-pre">{{ lecture.code || '(empty)' }}</pre>
          <button @click="useLectureCode" class="text-xs bg-sky-600 hover:bg-sky-700 text-white rounded-lg px-3 py-1">Copy into my editor</button>
        </div>
      </div>

      <!-- lecturing mode: teacher watches students' live code -->
      <div v-if="isStaff && board?.lecturingMode" class="mt-4 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden">
        <button @click="showMonitor = !showMonitor"
                class="w-full flex items-center justify-between px-3 py-2 text-sm font-semibold bg-slate-50 dark:bg-slate-800/60">
          <span>👀 Student code · {{ studentList.length }} live</span>
          <span class="text-xs">{{ showMonitor ? '▾' : '▸' }}</span>
        </button>
        <div v-if="showMonitor" class="divide-y divide-slate-100 dark:divide-slate-800">
          <div v-for="s in studentList" :key="s.uid">
            <button @click="openStudent = openStudent === s.uid ? null : s.uid"
                    class="w-full flex items-center gap-2 px-3 py-1.5 text-sm text-left hover:bg-slate-50 dark:hover:bg-slate-800/40">
              <span class="flex-1 truncate">{{ s.authorName }}</span>
              <span class="text-[11px] text-slate-400 dark:text-slate-500">{{ ago(s.updatedAt) }} ago</span>
              <span class="text-xs text-slate-400">{{ openStudent === s.uid ? '▾' : '▸' }}</span>
            </button>
            <pre v-if="openStudent === s.uid"
                 class="bg-slate-900 text-slate-100 dark:bg-black rounded-lg m-2 p-2 font-mono text-xs overflow-auto max-h-72 whitespace-pre">{{ s.code || '(empty)' }}</pre>
          </div>
          <p v-if="!studentList.length" class="px-3 py-2 text-sm text-slate-400 dark:text-slate-500">No student is typing yet.</p>
        </div>
      </div>

      <AiHint :problem-id="pid" :language="solveLang" :code="code" :stdin="stdin"
              :verdict="latestMine?.status === 'Done' ? latestMine?.verdict : ''"
              :compiler-output="runOut && !runOut.compileOk ? runOut.compilerOutput : (latestMine?.compilerOutput || '')"
              :stderr="runOut?.stderr || ''" />

      <h3 class="font-semibold text-sm mt-5 mb-2">Submissions</h3>
      <div class="space-y-1 max-h-64 overflow-y-auto pr-1">
        <div v-for="s in submissions" :key="s.id"
             class="flex items-center gap-2 text-sm border border-slate-100 dark:border-slate-800 rounded-lg px-2 py-1.5">
          <VerdictBadge :verdict="s.status === 'Done' ? s.verdict : s.status" small />
          <span class="text-slate-500 dark:text-slate-400">{{ s.authorName }}</span>
          <span v-if="s.status === 'Done'" class="text-xs text-slate-400 dark:text-slate-500">
            {{ s.runtimeMs }}ms · {{ s.memoryKb }}KB · {{ Math.round(s.score * 100) }}%
          </span>
        </div>
        <p v-if="!submissions.length" class="text-slate-400 dark:text-slate-500 text-sm">No submissions yet.</p>
      </div>
    </div>
    </template>

    <template #b>
    <!-- Right: editor + console -->
    <SplitPane direction="vertical" storage-key="beecoding.split.solve-console" :initial="66" :min="110">
      <template #a>
        <MonacoEditor v-model="code" :language="solveLang" :lsp="solveLang" :filename="props.problemSlug" />
      </template>
      <template #b>
      <div class="h-full overflow-y-auto border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-3 space-y-2">
        <div class="flex gap-2 items-center">
          <button @click="run" :disabled="running"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ running ? 'Running…' : 'Run' }}
          </button>
          <button @click="submit" :disabled="submitting"
                  class="bg-amber-500 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ submitting ? 'Submitting…' : 'Submit' }}
          </button>
          <span v-if="langs.length > 1" class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="l in langs" :key="l" @click="setLang(l)"
                    class="px-2.5 py-1"
                    :class="solveLang === l
                      ? 'bg-slate-800 text-white dark:bg-slate-600'
                      : 'text-slate-500 dark:text-slate-400'">
              {{ l === 'c' ? 'C' : 'C++' }}
            </button>
          </span>
        </div>
        <div v-if="testProgress" class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
          <span class="flex-1 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
            <span class="block h-full bg-amber-400 transition-all duration-300 ease-out"
                  :style="{ width: (testProgress.current / testProgress.total * 100) + '%' }"></span>
          </span>
          <span class="tabular-nums">Testcase {{ testProgress.current }}/{{ testProgress.total }}</span>
        </div>
        <div v-else-if="submittingId" class="text-xs text-slate-400 dark:text-slate-500 animate-pulse">
          Still checking your last submission…
        </div>
        <div class="grid grid-cols-2 gap-2">
          <div>
            <label class="text-xs text-slate-400 dark:text-slate-500">stdin</label>
            <textarea v-model="stdin"
                      class="w-full h-20 resize-y border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono text-xs"></textarea>
          </div>
          <div>
            <label class="text-xs text-slate-400 dark:text-slate-500">output</label>
            <pre class="w-full h-20 bg-slate-900 text-slate-100 dark:bg-black dark:border dark:border-slate-800 rounded-lg px-2 py-1 font-mono text-xs overflow-auto whitespace-pre-wrap">{{
              runOut
                ? (runOut.compileOk
                    ? (runOut.stdout || '') + (runOut.stderr ? '\n[stderr] ' + runOut.stderr : '') +
                      `\n— ${runOut.runtimeMs}ms, ${runOut.memoryKb}KB${runOut.timedOut ? ', TIMED OUT' : ''}`
                    : '[compile error]\n' + runOut.compilerOutput)
                : ''
            }}</pre>
          </div>
        </div>
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      </div>
      </template>
    </SplitPane>
    </template>
   </SplitPane>
  </div>
</template>
