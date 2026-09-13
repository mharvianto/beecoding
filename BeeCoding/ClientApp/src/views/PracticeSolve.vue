<script setup>
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useProgress } from '../stores/progress';
import { createBoardConnection } from '../lib/signalr';
import MonacoEditor from '../components/MonacoEditor.vue';
import VerdictBadge from '../components/VerdictBadge.vue';
import LevelBadge from '../components/LevelBadge.vue';
import ContentGuard from '../components/ContentGuard.vue';
import StatementImage from '../components/StatementImage.vue';
import AiHint from '../components/AiHint.vue';
import SplitPane from '../components/SplitPane.vue';
import { CODE_TEMPLATES, isPristine, allowedLangs, langLabel } from '../lib/templates';
import { loadDraft, saveDraft, clearDraft } from '../lib/draft';
import { celebrate } from '../lib/confetti';

const props = defineProps({ slug: { type: String, required: true } });
const auth = useAuth();
const progress = useProgress();

const problem = ref(null);
// internal id of the loaded problem — for API calls / SignalR that key by int id
const pid = computed(() => problem.value?.id);
const code = ref('');
const stdin = ref('');
const solveLang = ref('cpp');   // 'c' | 'cpp'

const langs = computed(() => allowedLangs(problem.value?.allowedLanguages));
const langNote = computed(() =>
  problem.value?.allowedLanguages ? langLabel(problem.value.allowedLanguages) : '');

function setLang(l) {
  if (l === solveLang.value || !langs.value.includes(l)) return;
  solveLang.value = l;
  if (isPristine(code.value)) code.value = CODE_TEMPLATES[l] || '';
  try { localStorage.setItem('beecoding.lang', l); } catch { /* ignore */ }
}

// local autosave so an accidental refresh doesn't wipe the editor
const draftScope = computed(() => `bank:${props.slug}`);
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
const runOut = ref(null);
const running = ref(false);
const submitting = ref(false);
const submissions = ref([]);
const error = ref('');
const gained = ref(0);
let conn = null;

async function load() {
  problem.value = await api.get(`/api/practice/${props.slug}`);
  let pref = null;
  try { pref = localStorage.getItem('beecoding.lang'); } catch { /* ignore */ }
  const allowed = allowedLangs(problem.value.allowedLanguages);
  solveLang.value = allowed.includes(pref) ? pref : allowed[0];
  code.value = CODE_TEMPLATES[solveLang.value] || '';

  const d = loadDraft(auth.user?.id, draftScope.value);
  if (d && d.code.trim() && !isPristine(d.code)) {
    code.value = d.code;
    if (d.lang === 'c' || d.lang === 'cpp') solveLang.value = d.lang;
    restored.value = true;
    restoredAt.value = new Date(d.ts).toLocaleString();
  }

  if (problem.value.sampleTests?.[0]) stdin.value = problem.value.sampleTests[0].stdin;
  await loadSubs();
}
async function loadSubs() {
  submissions.value = await api.get(`/api/practice/${props.slug}/submissions`);
  problem.value.solved = submissions.value.some((s) => s.verdict === 'Accepted' && s.score >= 1);
}

async function run() {
  error.value = ''; running.value = true; runOut.value = null;
  try {
    runOut.value = await api.post('/api/run', { language: solveLang.value, code: code.value, stdin: stdin.value, bankProblemId: pid.value });
  } catch (e) { error.value = e.message; }
  finally { running.value = false; }
}

async function submit() {
  error.value = ''; submitting.value = true; gained.value = 0;
  try {
    const before = progress.xp;
    const alreadySolved = problem.value.solved;
    await api.post(`/api/practice/${props.slug}/submit`, { code: code.value, language: solveLang.value });
    await loadSubs();
    // give the judge a moment, then reconcile XP
    setTimeout(async () => {
      await loadSubs();
      await progress.refresh();
      if (!alreadySolved && progress.xp > before) gained.value = progress.xp - before;
    }, 1500);
  } catch (e) { error.value = e.message; }
  finally { submitting.value = false; }
}

watch(code, () => {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(saveDraftNow, 500);
});
watch(solveLang, saveDraftNow);
watch(gained, (v) => { if (v > 0) celebrate(); });

onMounted(async () => {
  try { await load(); } catch (e) { error.value = e.message; return; }
  window.addEventListener('beforeunload', saveDraftNow);
  progress.refresh();
  conn = createBoardConnection();
  conn.on('practiceResult', (dto) => {
    if (dto.bankProblemId === pid.value) loadSubs();
  });
  conn.on('progressBumped', (p) => {
    const before = progress.xp;
    progress.$patch({ ...p, ready: true });
    if (p.xp > before) gained.value = p.xp - before;
  });
  try { await conn.start(); } catch { /* realtime best-effort */ }
});
onBeforeUnmount(async () => {
  clearTimeout(saveTimer);
  saveDraftNow();
  window.removeEventListener('beforeunload', saveDraftNow);
  try { await conn?.stop(); } catch {}
});
</script>

<template>
  <div v-if="problem" class="h-full">
   <SplitPane direction="horizontal" storage-key="beecoding.split.solve-main" :initial="42" :initial-stacked="34" :min="260">
    <template #a>
    <div class="h-full overflow-y-auto p-5 border-r border-slate-200 dark:border-slate-800">
      <RouterLink to="/practice" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to Practice</RouterLink>
      <div class="flex items-center gap-2 mt-2 mb-1 flex-wrap">
        <h1 class="text-lg font-bold">{{ problem.title }}</h1>
        <LevelBadge :level="problem.level" />
        <span v-if="problem.solved" class="text-[10px] px-1.5 py-0.5 rounded bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">✓ solved</span>
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

      <div v-if="gained" class="mb-3 text-sm bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-200 rounded-lg px-3 py-2">
        🎉 +{{ gained }} XP! Now Lv {{ progress.level }} · {{ progress.xp }} XP
      </div>

      <p class="text-[11px] text-amber-600 dark:text-amber-400 mb-2">
        🔒 Protected problem — served as an encrypted image watermarked with your identity.
      </p>
      <ContentGuard :active="true" :watermark="''">
        <StatementImage :bank-id="pid" />
      </ContentGuard>

      <div v-if="problem.sampleTests?.length" class="mt-3 flex items-center gap-2 flex-wrap">
        <span class="text-xs text-slate-400 dark:text-slate-500">Load sample input:</span>
        <button v-for="(t, i) in problem.sampleTests" :key="i" @click="stdin = t.stdin"
                class="text-xs px-2 py-0.5 rounded border border-slate-300 dark:border-slate-700 hover:border-amber-400">
          Sample {{ i + 1 }}
        </button>
      </div>

      <AiHint :bank-problem-id="pid" :language="solveLang" :code="code" :stdin="stdin"
              :verdict="submissions[0]?.status === 'Done' ? submissions[0]?.verdict : ''"
              :compiler-output="runOut && !runOut.compileOk ? runOut.compilerOutput : (submissions[0]?.compilerOutput || '')"
              :stderr="runOut?.stderr || ''" />

      <h3 class="font-semibold text-sm mt-5 mb-2">History</h3>
      <div class="space-y-1">
        <div v-for="s in submissions" :key="s.id"
             class="flex items-center gap-2 text-sm border border-slate-100 dark:border-slate-800 rounded-lg px-2 py-1.5">
          <VerdictBadge :verdict="s.status === 'Done' ? s.verdict : s.status" small />
          <span v-if="s.status === 'Done'" class="text-xs text-slate-400 dark:text-slate-500">
            {{ s.runtimeMs }}ms · {{ s.memoryKb }}KB · {{ Math.round(s.score * 100) }}%
          </span>
          <span class="text-xs text-slate-400 dark:text-slate-500 ml-auto">{{ new Date(s.createdAt + 'Z').toLocaleTimeString() }}</span>
        </div>
        <p v-if="!submissions.length" class="text-slate-400 dark:text-slate-500 text-sm">No submissions yet.</p>
      </div>
    </div>
    </template>

    <template #b>
    <SplitPane direction="vertical" storage-key="beecoding.split.solve-console" :initial="66" :min="110">
      <template #a>
        <MonacoEditor v-model="code" :language="solveLang" :lsp="solveLang" />
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
                    :class="solveLang === l ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ l === 'c' ? 'C' : 'C++' }}
            </button>
          </span>
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
