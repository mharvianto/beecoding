<script setup>
import { agoShort as ago } from '../lib/timeAgo';
import { tr } from '../lib/i18n';
import RunOutput from '../components/RunOutput.vue';
import Icon from '../components/Icon.vue';
import { ref, computed, reactive, onMounted, onBeforeUnmount, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useConfirmDialog } from '../stores/confirmDialog';
import { createBoardConnection } from '../lib/signalr';
import MonacoEditor from '../components/MonacoEditor.vue';
import SplitPane from '../components/SplitPane.vue';
import StdinFileButton from '../components/StdinFileButton.vue';
import InputFileName from '../components/InputFileName.vue';
import AiHint from '../components/AiHint.vue';
import { CODE_TEMPLATES, isPristine } from '../lib/templates';
import { editorFontSize } from '../lib/editorFont';
import { playgroundFocus } from '../lib/playgroundFocus';

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
const fileKey = computed(() => (storeKey.value ? storeKey.value + '.inputFile' : ''));
const langKey = computed(() => (storeKey.value ? storeKey.value + '.lang' : ''));
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
const inputFile = ref('');   // blank = pipe stdin; a name = write stdin to that file (local to this viewer)
const running = ref(false);
const runOut = ref(null);

let conn = null;
let pushTimer = null;
let saveTimer = null;
const confirmDialog = useConfirmDialog();

function pushSoon() {
  if (!conn || conn.state !== 'Connected') return;
  clearTimeout(pushTimer);
  pushTimer = setTimeout(pushNow, 600);
}
// One presenter per board: while another teacher holds the slot we watch their code instead of pushing, and only
// "Take over" changes that. The server frees the slot when the presenter's page goes (teacher id 0) and enforces all of this.
const other = ref(null);             // { id, name } — the other teacher currently presenting
const presenting = ref(false);       // my last push was accepted and nobody has taken over since
const takenOver = ref(false);        // I was presenting and another teacher took over: say so
const peekStudents = ref(false);     // while watching the presenter: show the student panel instead
const observing = computed(() => isStaff.value && lecturingOn.value && !!other.value);

function noteOtherPresenter(id, name) {
  if (presenting.value) takenOver.value = true;
  presenting.value = false;
  other.value = { id, name };
}
function becamePresenter() {
  presenting.value = true;
  takenOver.value = false;
  other.value = null;
}
function ingestLecture(l) {
  if (!l || l.problemId !== SCRATCH) return;
  if (!isStaff.value) { lecture.value = l; return; }
  if (l.teacherId && l.teacherId !== auth.user?.id) {
    lecture.value = l;
    noteOtherPresenter(l.teacherId, l.teacherName);
  } else other.value = null;   // our own push (or a takeover by us), or the presenter left
}
async function takeOver() {
  const ok = await confirmDialog.ask(tr('Take over from {name}? Students will see your editor instead of theirs.', { name: other.value?.name }),
    { confirmLabel: tr('Take over'), danger: false });
  if (!ok) return;
  try {
    const res = await conn.invoke('PushLecture', board.value.id, SCRATCH, code.value, liveLang.value, stdin.value, true);
    if (res?.accepted) becamePresenter();
  } catch { /* ignore */ }
}

function pushNow() {
  if (!conn || conn.state !== 'Connected') return;
  if (isStaff.value) {
    if (lecturingOn.value && !observing.value)
      conn.invoke('PushLecture', board.value.id, SCRATCH, code.value, liveLang.value, stdin.value, false)
        .then((res) => {
          if (res?.accepted) becamePresenter();
          else if (res?.presenterId) noteOtherPresenter(res.presenterId, res.presenterName);
        })
        .catch(() => {});
  } else {
    conn.invoke('PushDraft', board.value.id, SCRATCH, code.value).catch(() => {});
  }
}

function setLang(l) {
  if (l === liveLang.value) return;
  // swap the boilerplate only if nothing of one's own has been written yet (same as the Solve pages)
  if (isPristine(code.value)) code.value = CODE_TEMPLATES[l] || '';
  liveLang.value = l;
  try { localStorage.setItem('beecoding.lang', l); } catch { /* ignore */ }   // the account-wide default language
}
function resetTemplate() {
  code.value = CODE_TEMPLATES[liveLang.value] || '';
}
async function loadTeacherCode() {
  const l = lecture.value;
  if (!l) return;
  if (!isPristine(code.value) && code.value !== (l.code || '')
      && !(await confirmDialog.ask(tr('Replace the code in your editor with the code of {name}?', { name: l.teacherName }), { confirmLabel: tr('Replace'), danger: false }))) return;
  code.value = l.code || '';
  if (l.language === 'c' || l.language === 'cpp') liveLang.value = l.language;
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
  try { runOut.value = await api.post('/api/run', { language: liveLang.value, code: code.value, stdin: stdin.value, inputFileName: inputFile.value || null }); }
  catch (e) { error.value = e.message; }
  finally { running.value = false; }
}

const compilerOutput = computed(() => (runOut.value && !runOut.value.compileOk ? runOut.value.compilerOutput : ''));

watch(code, () => {
  pushSoon();
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try { if (storeKey.value) localStorage.setItem(storeKey.value, code.value); } catch { /* ignore */ }
  }, 400);
});
watch(liveLang, () => {
  pushSoon();
  try { if (langKey.value) localStorage.setItem(langKey.value, liveLang.value); } catch { /* ignore */ }
});
watch(stdin, () => {
  if (isStaff.value) pushSoon();
  try { if (stdinKey.value) localStorage.setItem(stdinKey.value, stdin.value); } catch { /* ignore */ }
});
watch(inputFile, () => {
  try { if (fileKey.value) localStorage.setItem(fileKey.value, inputFile.value); } catch { /* ignore */ }
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

  // Language: what this board's live editor last used, else the account's default language (Solve/Practice keep it
  // in beecoding.lang, synced across devices), else C++.
  try {
    const isLang = (l) => (l === 'c' || l === 'cpp' ? l : null);
    liveLang.value = isLang(localStorage.getItem(langKey.value)) || isLang(localStorage.getItem('beecoding.lang')) || 'cpp';
    code.value = CODE_TEMPLATES[liveLang.value] || code.value;
  } catch { /* ignore */ }
  try {
    const saved = localStorage.getItem(storeKey.value);
    if (saved && saved.trim() && !isPristine(saved)) code.value = saved;   // an untouched template follows the language
  } catch { /* ignore */ }
  try {
    const s = localStorage.getItem(stdinKey.value);
    if (s != null) stdin.value = s;
  } catch { /* ignore */ }
  try { inputFile.value = localStorage.getItem(fileKey.value) || ''; } catch { /* ignore */ }
  try { showStudents.value = localStorage.getItem(stKey.value) !== '0'; } catch { /* ignore */ }

  conn = createBoardConnection();
  conn.on('boardSettingsChanged', async () => {
    const was = board.value?.role;
    try { board.value = await api.get(`/api/boards/${props.slug}`); } catch { /* ignore */ }
    if (was === 'Student' && board.value?.role !== 'Student') window.location.reload();   // lifted to teacher: reconnect to join the staff channel
  });
  conn.on('lectureUpdated', ingestLecture);
  conn.on('draftUpdated', (d) => { if (isStaff.value) ingestDraft(d); });
  try {
    await conn.start();
    await conn.invoke('JoinBoard', board.value.id);
    if (isStaff.value) {
      // See who presented last before pushing our own buffer: someone else's code stays on the students' screens
      // until we edit (or take over), merely opening the page must not replace it.
      let last = null;
      try { last = await conn.invoke('GetLecture', board.value.id, SCRATCH); ingestLecture(last); } catch { /* ignore */ }
      if (!last || last.teacherId === auth.user?.id) pushNow();
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
      <RouterLink :to="`/boards/${props.slug}`" class="text-sm text-slate-500 dark:text-slate-400">← {{ tr('back to board') }}</RouterLink>
      <h1 class="text-sm font-bold"><Icon name="video" /> {{ tr('Live code · {title}', { title: board.title }) }}</h1>
      <span v-if="isStaff"
            class="text-[11px] px-2 py-0.5 rounded-full"
            :class="lecturingOn
              ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
              : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'">
        {{ !lecturingOn ? tr('not broadcasting') : observing ? tr('students are watching {name}', { name: other.name }) : tr('students are watching') }}
      </span>
      <div class="ml-auto flex items-center gap-2">
        <button v-if="isStaff" @click="toggleStudentsPanel"
                class="text-xs px-2.5 py-1 rounded-lg font-medium border"
                :class="showStudents
                  ? 'bg-slate-800 text-white border-slate-800 dark:bg-slate-600 dark:border-slate-600'
                  : 'border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300'">
          <Icon name="eye" /> {{ tr('Student panel') }} {{ showStudents ? tr('on') : tr('off') }}
        </button>
        <button v-if="isStaff" @click="toggleLecturing"
                class="text-xs px-2.5 py-1 rounded-lg font-medium"
                :class="lecturingOn
                  ? 'bg-amber-500 text-slate-900'
                  : 'border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300'">
          {{ lecturingOn ? tr('Stop broadcasting') : tr('Start broadcasting') }}
        </button>
        <button @click="playgroundFocus = !playgroundFocus"
                :title="playgroundFocus ? tr('Show the top navigation bar') : tr('Hide the top navigation bar for more room')"
                class="text-xs px-2.5 py-1 rounded-lg border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300">
          {{ playgroundFocus ? tr('⤡ Show navbar') : tr('⤢ Full screen') }}
        </button>
        <button @click="resetTemplate"
                class="text-xs px-2.5 py-1 rounded-lg border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300">
          {{ tr('Reset template') }}
        </button>
        <span class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
          <button v-for="l in ['c', 'cpp']" :key="l" @click="setLang(l)" class="px-2.5 py-1"
                  :class="liveLang === l ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
            {{ l === 'c' ? 'C' : 'C++' }}
          </button>
        </span>
      </div>
    </div>

    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400 px-4 py-2">{{ tr(error) }}</p>

    <div v-if="observing" :role="takenOver ? 'alert' : 'status'"
         class="flex items-center gap-2 flex-wrap text-sm px-4 py-2 border-b"
         :class="takenOver
           ? 'bg-amber-100 text-amber-900 border-amber-300 dark:bg-amber-500/20 dark:text-amber-200 dark:border-amber-500/40 font-semibold'
           : 'bg-sky-50 text-sky-700 border-sky-100 dark:bg-sky-500/10 dark:text-sky-300 dark:border-sky-500/20'">
      <span><Icon name="present" /> {{ takenOver
        ? tr('{name} took over the broadcast — students now see their editor, not yours.', { name: other.name })
        : tr('{name} is presenting — your editor is not shown to students.', { name: other.name }) }}</span>
      <button @click="loadTeacherCode" class="underline hover:no-underline">{{ tr('Copy their code') }}</button>
      <button @click="takeOver" class="ml-auto px-2.5 py-1 rounded-lg bg-sky-600 text-white text-xs font-medium">{{ tr('Take over') }}</button>
    </div>

    <!-- Teacher: left = my code + stdin + AI tutor, right = a student's code -->
    <div v-if="isStaff" class="flex-1 min-h-0">
      <SplitPane direction="horizontal" storage-key="beecoding.split.livecode-main"
                 :initial="55" :initial-stacked="50" :min="320" :hide-b="!showStudents && !observing">
        <template #a>
          <SplitPane direction="vertical" foldable storage-key="beecoding.split.livecode" :initial="60" :min="110">
            <template #a>
              <MonacoEditor v-model="code" :language="liveLang" :lsp="liveLang" />
            </template>
            <template #b>
              <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2 border-r border-slate-200 dark:border-slate-800">
                <div class="flex gap-2 items-center">
                  <button @click="run" :disabled="running"
                          class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
                    {{ running ? tr('Running…') : tr('Run') }}
                  </button>
                  <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Run only — nothing is graded.') }}</span>
                </div>
                <div class="grid grid-cols-2 gap-2">
                  <div>
                    <div class="flex items-center justify-between gap-2">
                      <label class="text-xs text-slate-500 dark:text-slate-400">{{ tr('stdin') }}</label>
                      <span class="inline-flex items-center gap-3">
          <InputFileName v-model="inputFile" />
          <StdinFileButton @load="stdin = $event" />
        </span>
                    </div>
                    <textarea :aria-label="tr('Input (stdin)')" v-model="stdin" :style="{ fontSize: editorFontSize + 'px' }"
                              class="w-full h-20 resize-y border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono"></textarea>
                  </div>
                  <div>
                    <RunOutput :result="runOut" :font-size="editorFontSize" :running="running" @run="run" />
                  </div>
                </div>
                <AiHint :board-slug="props.slug" :language="liveLang" :code="code" :stdin="stdin"
                        :compiler-output="compilerOutput" :stderr="runOut?.stderr || ''" />
              </div>
            </template>
          </SplitPane>
        </template>

        <template #b>
          <!-- another teacher presents: their code, live and read-only, beside my editor -->
          <div v-if="observing && !peekStudents" class="h-full flex flex-col bg-white dark:bg-slate-900">
            <div class="flex items-center gap-2 px-3 py-2 border-b border-slate-200 dark:border-slate-800 text-sm">
              <span class="font-semibold truncate"><Icon name="present" /> {{ tr("{name}'s code · live", { name: other.name }) }}</span>
              <button @click="peekStudents = true" class="ml-auto shrink-0 text-xs underline hover:no-underline">{{ tr('Show student code') }}</button>
            </div>
            <MonacoEditor v-if="lecture" :model-value="lecture.code" :language="lecture.language || 'cpp'" :read-only="true" class="flex-1 min-h-0" />
            <pre v-if="lecture?.stdin" tabindex="0" :style="{ fontSize: editorFontSize + 'px' }"
                 class="m-2 max-h-24 overflow-auto bg-slate-100 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono whitespace-pre-wrap">{{ lecture.stdin }}</pre>
          </div>
          <div v-else class="h-full flex flex-col bg-white dark:bg-slate-900">
            <div class="flex items-center gap-2 px-3 py-2 border-b border-slate-200 dark:border-slate-800 text-sm">
              <button v-if="observing" @click="peekStudents = false" class="shrink-0 text-xs underline hover:no-underline">{{ tr("{name}'s code · live", { name: other.name }) }}</button>
              <span class="font-semibold"><Icon name="eye" /> {{ tr('Student code') }}</span>
              <span class="text-[11px] text-slate-500 dark:text-slate-400">{{ tr('{n} live', { n: studentList.length }) }}</span>
              <select :aria-label="tr('Student')" v-if="studentList.length" v-model="selectedUid"
                      class="ml-auto text-xs border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 max-w-[50%]">
                <option v-for="s in studentList" :key="s.uid" :value="s.uid">
                  {{ s.authorName }} · {{ ago(s.updatedAt) }}
                </option>
              </select>
              <button @click="toggleStudentsPanel" :title="tr('Hide this panel')"
                      class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200"
                      :class="{ 'ml-auto': !studentList.length }" :aria-label="tr('Close')">✕</button>
            </div>
            <MonacoEditor v-if="selectedStudent" :model-value="selectedStudent.code"
                          :language="liveLang" :read-only="true" class="flex-1 min-h-0" />
            <div v-else class="flex-1 grid place-items-center text-sm text-slate-500 dark:text-slate-400 p-6 text-center">
              {{ tr('No student is typing yet.') }}
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
                       : 'bg-slate-50 dark:bg-slate-800/50 text-slate-500 dark:text-slate-400'">
                  <template v-if="lecturingOn && lecture">
                    <span class="truncate"><Icon name="present" /> {{ lecture.teacherName }} · {{ lecture.language === 'c' ? 'C' : 'C++' }} · {{ ago(lecture.updatedAt) }}</span>
                    <button @click="loadTeacherCode" class="ml-auto shrink-0 bg-sky-600 hover:bg-sky-700 text-white rounded-lg px-2.5 py-0.5">{{ tr('Load into my editor') }}</button>
                  </template>
                  <span v-else>{{ lecturingOn ? tr('Waiting for the teacher to start typing…') : tr('The teacher hasn’t started a live session yet.') }}</span>
                </div>
                <MonacoEditor v-if="lecture" :model-value="lecture.code" :language="lecture.language || 'cpp'"
                              :read-only="true" class="flex-1 min-h-0" />
                <div v-else class="flex-1 grid place-items-center text-sm text-slate-500 dark:text-slate-400 p-6 text-center">
                  {{ tr('The teacher’s code will show here.') }}
                </div>
              </div>
            </template>
            <template #b>
              <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2 border-r border-slate-200 dark:border-slate-800">
                <div v-if="lecture && lecture.stdin">
                  <div class="flex items-center gap-2">
                    <label class="text-xs text-slate-500 dark:text-slate-400">{{ tr('teacher\'s stdin') }}</label>
                    <button @click="useTeacherStdin"
                            class="ml-auto text-[11px] px-2 py-0.5 rounded-lg border border-sky-300 dark:border-sky-500/40 text-sky-700 dark:text-sky-300 hover:bg-sky-50 dark:hover:bg-sky-500/10">
                      {{ stdinCopied ? tr('Copied ✓') : tr('Use this stdin') }}
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
          <SplitPane direction="vertical" foldable storage-key="beecoding.split.livecode-student" :initial="58" :min="110">
            <template #a>
              <MonacoEditor v-model="code" :language="liveLang" :lsp="liveLang" />
            </template>
            <template #b>
              <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2">
                <div class="flex gap-2 items-center">
                  <button @click="run" :disabled="running"
                          class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
                    {{ running ? tr('Running…') : tr('Run') }}
                  </button>
                  <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Your own editor — nothing is graded.') }}</span>
                </div>
                <div class="grid grid-cols-2 gap-2">
                  <div>
                    <div class="flex items-center justify-between gap-2">
                      <label class="text-xs text-slate-500 dark:text-slate-400">{{ tr('stdin') }}</label>
                      <span class="inline-flex items-center gap-3">
          <InputFileName v-model="inputFile" />
          <StdinFileButton @load="stdin = $event" />
        </span>
                    </div>
                    <textarea :aria-label="tr('Input (stdin)')" v-model="stdin" :style="{ fontSize: editorFontSize + 'px' }"
                              class="w-full h-20 resize-y border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 font-mono"></textarea>
                  </div>
                  <div>
                    <RunOutput :result="runOut" :font-size="editorFontSize" :running="running" @run="run" />
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
