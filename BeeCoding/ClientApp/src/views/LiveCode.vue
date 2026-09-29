<script setup>
import { ref, computed, reactive, onMounted, onBeforeUnmount, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { createBoardConnection } from '../lib/signalr';
import MonacoEditor from '../components/MonacoEditor.vue';
import SplitPane from '../components/SplitPane.vue';
import AiHint from '../components/AiHint.vue';
import { CODE_TEMPLATES } from '../lib/templates';
import { editorFontSize } from '../lib/editorFont';

// A board-wide live-coding scratchpad — lecturing mode without a problem.
// Streamed with problemId 0 so it never collides with a real problem's buffers.
const SCRATCH = 0;

const props = defineProps({ slug: { type: String, required: true } });
const auth = useAuth();

const board = ref(null);
const error = ref('');
const isStaff = computed(() => board.value && board.value.role !== 'Student');
const lecturingOn = computed(() => !!board.value?.lecturingMode);

const storeKey = computed(() =>
  !board.value ? '' : `beecoding.livecode.${isStaff.value ? '' : 'student.'}${board.value.id}`);
const stdinKey = computed(() => (storeKey.value ? storeKey.value + '.stdin' : ''));
const code = ref(CODE_TEMPLATES.cpp);
const liveLang = ref('cpp');

// student view of the teacher's buffer
const lecture = ref(null);

// teacher view of students' buffers
const studentDrafts = reactive({});   // userId -> { authorName, code, updatedAt }
const selectedUid = ref(null);
const showStudents = ref(true);       // teacher: show/hide the whole "Student code" panel
const stKey = computed(() => (board.value ? `beecoding.livecode.showstudents.${board.value.id}` : ''));
function toggleStudentsPanel() {
  showStudents.value = !showStudents.value;
  try { localStorage.setItem(stKey.value, showStudents.value ? '1' : '0'); } catch { /* ignore */ }
}
const studentList = computed(() =>
  Object.entries(studentDrafts)
    .map(([uid, d]) => ({ uid, ...d }))
    .sort((a, b) => (b.updatedAt || '').localeCompare(a.updatedAt || '')));
const selectedStudent = computed(() =>
  studentList.value.find((s) => String(s.uid) === String(selectedUid.value)) || studentList.value[0] || null);

// run panel
const stdin = ref('');
const running = ref(false);
const runOut = ref(null);

let conn = null;
let pushTimer = null;
let saveTimer = null;

function pushSoon() {
  if (!conn || conn.state !== 'Connected') return;
  clearTimeout(pushTimer);
  pushTimer = setTimeout(pushNow, 600);
}
function pushNow() {
  if (!conn || conn.state !== 'Connected') return;
  if (isStaff.value) {
    if (lecturingOn.value)
      conn.invoke('PushLecture', board.value.id, SCRATCH, code.value, liveLang.value, stdin.value).catch(() => {});
  } else {
    conn.invoke('PushDraft', board.value.id, SCRATCH, code.value).catch(() => {});
  }
}

function setLang(l) {
  if (l === liveLang.value) return;
  liveLang.value = l;
}
function loadTeacherCode() {
  if (!lecture.value) return;
  code.value = lecture.value.code || '';
  if (lecture.value.language === 'c' || lecture.value.language === 'cpp') liveLang.value = lecture.value.language;
}
const stdinCopied = ref(false);
function useTeacherStdin() {
  if (!lecture.value?.stdin) return;
  stdin.value = lecture.value.stdin;
  stdinCopied.value = true;
  setTimeout(() => (stdinCopied.value = false), 1500);
}

async function toggleLecturing() {
  try { board.value = await api.patch(`/api/boards/${props.slug}`, { lecturingMode: !lecturingOn.value }); }
  catch (e) { error.value = e.message; }
}

async function run() {
  error.value = ''; running.value = true; runOut.value = null;
  try { runOut.value = await api.post('/api/run', { language: liveLang.value, code: code.value, stdin: stdin.value }); }
  catch (e) { error.value = e.message; }
  finally { running.value = false; }
}

function ago(ts) {
  if (!ts) return '';
  const s = Math.max(0, (Date.now() - new Date(ts + (ts.endsWith('Z') ? '' : 'Z')).getTime()) / 1000);
  return s < 60 ? `${s | 0}s` : s < 3600 ? `${(s / 60) | 0}m` : `${(s / 3600) | 0}h`;
}

const compilerOutput = computed(() => (runOut.value && !runOut.value.compileOk ? runOut.value.compilerOutput : ''));

watch(code, () => {
  pushSoon();
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try { if (storeKey.value) localStorage.setItem(storeKey.value, code.value); } catch { /* ignore */ }
  }, 400);
});
watch(liveLang, pushSoon);
watch(stdin, () => {
  if (isStaff.value) pushSoon();
  try { if (stdinKey.value) localStorage.setItem(stdinKey.value, stdin.value); } catch { /* ignore */ }
});
watch(lecturingOn, (on) => { if (on) pushNow(); });

function ingestDraft(d) {
  if (!d || d.problemId !== SCRATCH || d.userId === auth.user?.id) return;
  studentDrafts[d.userId] = { authorName: d.authorName, code: d.code, updatedAt: d.updatedAt };
  if (selectedUid.value == null) selectedUid.value = d.userId;
}

onMounted(async () => {
  try { board.value = await api.get(`/api/boards/${props.slug}`); }
  catch (e) { error.value = e.message; return; }

  try {
    const saved = localStorage.getItem(storeKey.value);
    if (saved && saved.trim()) code.value = saved;
  } catch { /* ignore */ }
  try {
    const s = localStorage.getItem(stdinKey.value);
    if (s != null) stdin.value = s;
  } catch { /* ignore */ }
  try { showStudents.value = localStorage.getItem(stKey.value) !== '0'; } catch { /* ignore */ }

  conn = createBoardConnection();
  conn.on('boardSettingsChanged', async () => {
    try { board.value = await api.get(`/api/boards/${props.slug}`); } catch { /* ignore */ }
  });
  conn.on('lectureUpdated', (l) => {
    if (l.problemId === SCRATCH && !isStaff.value) lecture.value = l;
  });
  conn.on('draftUpdated', (d) => { if (isStaff.value) ingestDraft(d); });
  try {
    await conn.start();
    await conn.invoke('JoinBoard', board.value.id);
    if (isStaff.value) {
      pushNow();
      try {
        const ds = await conn.invoke('GetDrafts', board.value.id);
        (ds || []).forEach(ingestDraft);
      } catch { /* ignore */ }
    } else {
      const l = await conn.invoke('GetLecture', board.value.id, SCRATCH);
      if (l) lecture.value = l;
      pushNow();
    }
  } catch { /* ignore */ }
});
onBeforeUnmount(async () => {
  clearTimeout(pushTimer);
  clearTimeout(saveTimer);
  try { await conn?.stop(); } catch { /* ignore */ }
});
</script>

<template>
  <div v-if="board" class="h-full flex flex-col">
    <div class="flex items-center gap-3 px-4 py-2 border-b border-slate-200 dark:border-slate-800 flex-wrap">
      <RouterLink :to="`/boards/${props.slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to board</RouterLink>
      <h1 class="text-sm font-bold">🎥 Live code · {{ board.title }}</h1>
      <span v-if="isStaff"
            class="text-[11px] px-2 py-0.5 rounded-full"
            :class="lecturingOn
              ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
              : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
        {{ lecturingOn ? 'students are watching' : 'not broadcasting' }}
      </span>
      <div class="ml-auto flex items-center gap-2">
        <button v-if="isStaff" @click="toggleStudentsPanel"
                class="text-xs px-2.5 py-1 rounded-lg font-medium border"
                :class="showStudents
                  ? 'bg-slate-800 text-white border-slate-800 dark:bg-slate-600 dark:border-slate-600'
                  : 'border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300'">
          👀 Student panel {{ showStudents ? 'on' : 'off' }}
        </button>
        <button v-if="isStaff" @click="toggleLecturing"
                class="text-xs px-2.5 py-1 rounded-lg font-medium"
                :class="lecturingOn
                  ? 'bg-amber-500 text-white'
                  : 'border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300'">
          {{ lecturingOn ? 'Stop broadcasting' : 'Start broadcasting' }}
        </button>
        <span class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
          <button v-for="l in ['c', 'cpp']" :key="l" @click="setLang(l)" class="px-2.5 py-1"
                  :class="liveLang === l ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
            {{ l === 'c' ? 'C' : 'C++' }}
          </button>
        </span>
      </div>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 px-4 py-2">{{ error }}</p>

    <!-- Teacher: left = my code + stdin + AI tutor, right = a student's code -->
    <div v-if="isStaff" class="flex-1 min-h-0">
      <SplitPane direction="horizontal" storage-key="beecoding.split.livecode-main"
                 :initial="55" :initial-stacked="50" :min="320" :hide-b="!showStudents">
        <template #a>
          <SplitPane direction="vertical" storage-key="beecoding.split.livecode" :initial="60" :min="110">
            <template #a>
              <MonacoEditor v-model="code" :language="liveLang" :lsp="liveLang" />
            </template>
            <template #b>
              <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2 border-r border-slate-200 dark:border-slate-800">
                <div class="flex gap-2 items-center">
                  <button @click="run" :disabled="running"
                          class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
                    {{ running ? 'Running…' : 'Run' }}
                  </button>
                  <span class="text-xs text-slate-400 dark:text-slate-500">Run only — nothing is graded.</span>
                </div>
                <div class="grid grid-cols-2 gap-2">
                  <div>
                    <label class="text-xs text-slate-400 dark:text-slate-500">stdin</label>
                    <textarea v-model="stdin" :style="{ fontSize: editorFontSize + 'px' }"
                              class="w-full h-20 resize-y border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono"></textarea>
                  </div>
                  <div>
                    <label class="text-xs text-slate-400 dark:text-slate-500">output</label>
                    <pre :style="{ fontSize: editorFontSize + 'px' }"
                         class="w-full h-20 bg-slate-900 text-slate-100 dark:bg-black dark:border dark:border-slate-800 rounded-lg px-2 py-1 font-mono overflow-auto whitespace-pre-wrap">{{
                      runOut
                        ? (runOut.compileOk
                            ? (runOut.stdout || '') + (runOut.stderr ? '\n[stderr] ' + runOut.stderr : '') +
                              `\n— ${runOut.runtimeMs}ms, ${runOut.memoryKb}KB${runOut.timedOut ? ', TIMED OUT' : ''}`
                            : '[compile error]\n' + runOut.compilerOutput)
                        : ''
                    }}</pre>
                  </div>
                </div>
                <AiHint :board-slug="props.slug" :language="liveLang" :code="code" :stdin="stdin"
                        :compiler-output="compilerOutput" :stderr="runOut?.stderr || ''" />
              </div>
            </template>
          </SplitPane>
        </template>

        <template #b>
          <div class="h-full flex flex-col bg-white dark:bg-slate-900">
            <div class="flex items-center gap-2 px-3 py-2 border-b border-slate-200 dark:border-slate-800 text-sm">
              <span class="font-semibold">👀 Student code</span>
              <span class="text-[11px] text-slate-400 dark:text-slate-500">{{ studentList.length }} live</span>
              <select v-if="studentList.length" v-model="selectedUid"
                      class="ml-auto text-xs border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 max-w-[50%]">
                <option v-for="s in studentList" :key="s.uid" :value="s.uid">
                  {{ s.authorName }} · {{ ago(s.updatedAt) }} ago
                </option>
              </select>
              <button @click="toggleStudentsPanel" title="Hide this panel"
                      class="text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-200"
                      :class="{ 'ml-auto': !studentList.length }">✕</button>
            </div>
            <MonacoEditor v-if="selectedStudent" :model-value="selectedStudent.code"
                          :language="liveLang" :read-only="true" class="flex-1 min-h-0" />
            <div v-else class="flex-1 grid place-items-center text-sm text-slate-400 dark:text-slate-500 p-6 text-center">
              No student is typing yet.
            </div>
          </div>
        </template>
      </SplitPane>
    </div>

    <!-- Student: left = teacher's code + stdin + AI tutor, right = my own editor -->
    <div v-else class="flex-1 min-h-0">
      <SplitPane direction="horizontal" storage-key="beecoding.split.livecode-student-main"
                 :initial="48" :initial-stacked="45" :min="300">
        <template #a>
          <SplitPane direction="vertical" storage-key="beecoding.split.livecode-student-left" :initial="58" :min="110">
            <template #a>
              <div class="h-full flex flex-col border-r border-slate-200 dark:border-slate-800">
                <div class="flex items-center gap-2 px-3 py-1.5 text-xs shrink-0"
                     :class="lecturingOn && lecture
                       ? 'bg-sky-50 dark:bg-sky-500/10 text-sky-700 dark:text-sky-300'
                       : 'bg-slate-50 dark:bg-slate-800/50 text-slate-400 dark:text-slate-500'">
                  <template v-if="lecturingOn && lecture">
                    <span class="truncate">👨‍🏫 {{ lecture.teacherName }} · {{ lecture.language === 'c' ? 'C' : 'C++' }} · {{ ago(lecture.updatedAt) }} ago</span>
                    <button @click="loadTeacherCode" class="ml-auto shrink-0 bg-sky-600 hover:bg-sky-700 text-white rounded-lg px-2.5 py-0.5">Load into my editor</button>
                  </template>
                  <span v-else>{{ lecturingOn ? 'Waiting for the teacher to start typing…' : 'The teacher hasn’t started a live session yet.' }}</span>
                </div>
                <MonacoEditor v-if="lecture" :model-value="lecture.code" :language="lecture.language || 'cpp'"
                              :read-only="true" class="flex-1 min-h-0" />
                <div v-else class="flex-1 grid place-items-center text-sm text-slate-400 dark:text-slate-500 p-6 text-center">
                  The teacher’s code will show here.
                </div>
              </div>
            </template>
            <template #b>
              <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2 border-r border-slate-200 dark:border-slate-800">
                <div v-if="lecture && lecture.stdin">
                  <div class="flex items-center gap-2">
                    <label class="text-xs text-slate-400 dark:text-slate-500">teacher's stdin</label>
                    <button @click="useTeacherStdin"
                            class="ml-auto text-[11px] px-2 py-0.5 rounded-lg border border-sky-300 dark:border-sky-500/40 text-sky-700 dark:text-sky-300 hover:bg-sky-50 dark:hover:bg-sky-500/10">
                      {{ stdinCopied ? 'Copied ✓' : 'Use this stdin' }}
                    </button>
                  </div>
                  <pre :style="{ fontSize: editorFontSize + 'px' }"
                       class="w-full max-h-20 overflow-auto bg-slate-100 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono whitespace-pre-wrap">{{ lecture.stdin }}</pre>
                </div>
                <AiHint :board-slug="props.slug" :teacher-code="lecture?.code || ''"
                        :language="liveLang" :code="code" :stdin="stdin"
                        :compiler-output="compilerOutput" :stderr="runOut?.stderr || ''" />
              </div>
            </template>
          </SplitPane>
        </template>

        <template #b>
          <SplitPane direction="vertical" storage-key="beecoding.split.livecode-student" :initial="58" :min="110">
            <template #a>
              <MonacoEditor v-model="code" :language="liveLang" :lsp="liveLang" />
            </template>
            <template #b>
              <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2">
                <div class="flex gap-2 items-center">
                  <button @click="run" :disabled="running"
                          class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
                    {{ running ? 'Running…' : 'Run' }}
                  </button>
                  <span class="text-xs text-slate-400 dark:text-slate-500">Your own editor — nothing is graded.</span>
                </div>
                <div class="grid grid-cols-2 gap-2">
                  <div>
                    <label class="text-xs text-slate-400 dark:text-slate-500">stdin</label>
                    <textarea v-model="stdin" :style="{ fontSize: editorFontSize + 'px' }"
                              class="w-full h-20 resize-y border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono"></textarea>
                  </div>
                  <div>
                    <label class="text-xs text-slate-400 dark:text-slate-500">output</label>
                    <pre :style="{ fontSize: editorFontSize + 'px' }"
                         class="w-full h-20 bg-slate-900 text-slate-100 dark:bg-black dark:border dark:border-slate-800 rounded-lg px-2 py-1 font-mono overflow-auto whitespace-pre-wrap">{{
                      runOut
                        ? (runOut.compileOk
                            ? (runOut.stdout || '') + (runOut.stderr ? '\n[stderr] ' + runOut.stderr : '') +
                              `\n— ${runOut.runtimeMs}ms, ${runOut.memoryKb}KB${runOut.timedOut ? ', TIMED OUT' : ''}`
                            : '[compile error]\n' + runOut.compilerOutput)
                        : ''
                    }}</pre>
                  </div>
                </div>
              </div>
            </template>
          </SplitPane>
        </template>
      </SplitPane>
    </div>
  </div>
</template>
