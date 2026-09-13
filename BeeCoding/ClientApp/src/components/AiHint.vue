<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import MarkdownBlock from './MarkdownBlock.vue';

const props = defineProps({
  problemId: [String, Number],
  bankProblemId: [String, Number],
  boardSlug: { type: String, default: '' },     // live-coding session (no problem)
  teacherCode: { type: String, default: '' },   // teacher's live buffer, for "explain this"
  language: { type: String, default: 'cpp' },
  code: { type: String, default: '' },
  stdin: { type: String, default: '' },
  verdict: { type: String, default: '' },
  compilerOutput: { type: String, default: '' },
  stderr: { type: String, default: '' },
});

const liveMode = computed(() => !!props.boardSlug && !props.problemId && !props.bankProblemId);

const enabled = ref(false);
const open = ref(false);
const question = ref('');
const reply = ref('');
const busy = ref(false);
const elapsed = ref(0);   // seconds spent waiting on the current ask(), for the "Thinking…" timer
let elapsedTimer = null;
const error = ref('');
const level = ref(0);   // progressive hint level for this problem (1..4)
const LEVEL_LABEL = ['', 'a small nudge', 'more specific', 'step-by-step', 'detailed walkthrough'];

const LANGS = { id: 'Indonesian', en: 'English' };
const lang = ref('id');
try { lang.value = localStorage.getItem('beecoding.aiLang') || 'id'; } catch { /* ignore */ }
function setLang(l) {
  lang.value = l;
  try { localStorage.setItem('beecoding.aiLang', l); } catch { /* ignore */ }
}

onMounted(async () => {
  try {
    const r = await api.get('/api/ai/enabled');
    enabled.value = r?.enabled === true;
    if (r?.defaultLang && !localStorage.getItem('beecoding.aiLang')) lang.value = r.defaultLang;
  } catch { enabled.value = false; }
});
onUnmounted(() => clearInterval(elapsedTimer));

const idPayload = () => ({
  problemId: props.problemId ? Number(props.problemId) : null,
  bankProblemId: props.bankProblemId ? Number(props.bankProblemId) : null,
  boardSlug: props.boardSlug || null,
});

async function startOver() {
  try { await api.post('/api/ai/hint-progress/reset', idPayload()); } catch { /* ignore */ }
  level.value = 0;
  reply.value = '';
}

function payload(extra = {}) {
  return {
    problemId: props.problemId ? Number(props.problemId) : null,
    bankProblemId: props.bankProblemId ? Number(props.bankProblemId) : null,
    boardSlug: props.boardSlug || null,
    teacherCode: liveMode.value ? (props.teacherCode || null) : null,
    language: props.language,
    code: props.code,
    stdin: props.stdin,
    verdict: props.verdict,
    compilerOutput: props.compilerOutput,
    stderr: props.stderr,
    question: question.value.trim() || null,
    lang: lang.value,
    ...extra,
  };
}

async function ask(extra = {}) {
  error.value = ''; reply.value = ''; busy.value = true; level.value = 0;
  elapsed.value = 0;
  clearInterval(elapsedTimer);
  elapsedTimer = setInterval(() => { elapsed.value += 1; }, 1000);
  try {
    const res = await fetch(withBase('/api/ai/hint/stream'), {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload(extra)),
    });
    if (!res.ok) {
      if (res.status === 429) throw new Error('Give the AI tutor a few seconds between questions.');
      if (res.status === 404) { enabled.value = false; throw new Error('AI tutor is off.'); }
      throw new Error((await res.text()) || `HTTP ${res.status}`);
    }
    const reader = res.body.getReader();
    const dec = new TextDecoder();
    let buf = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buf += dec.decode(value, { stream: true });
      let i;
      while ((i = buf.indexOf('\n\n')) >= 0) {
        const frame = buf.slice(0, i);
        buf = buf.slice(i + 2);
        const dataLine = frame.split('\n').find((l) => l.startsWith('data:'));
        if (!dataLine) continue;
        let msg;
        try { msg = JSON.parse(dataLine.slice(5).trim()); } catch { continue; }
        if (msg.error) throw new Error(msg.error);
        if (msg.level != null) level.value = msg.level;
        if (msg.delta) reply.value += msg.delta;
        else if (msg.final != null && msg.final !== reply.value) reply.value = msg.final;
      }
    }
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
    clearInterval(elapsedTimer);
    elapsedTimer = null;
  }
}
</script>

<template>
  <div v-if="enabled" class="mt-5 border border-violet-200 dark:border-violet-500/30 rounded-xl overflow-hidden">
    <button @click="open = !open"
            class="w-full flex items-center justify-between px-3 py-2 text-sm font-semibold bg-violet-50 dark:bg-violet-500/10 text-violet-700 dark:text-violet-300">
      <span>🤖 AI tutor{{ liveMode ? '' : ' — hints only' }}</span>
      <span class="text-xs">{{ open ? '▾' : '▸' }}</span>
    </button>

    <div v-if="open" class="p-3 space-y-2">
      <p class="text-[11px] text-slate-400 dark:text-slate-500">
        <template v-if="liveMode">
          Ask what the teacher's code does, or get help with an error in your own follow-along code.
        </template>
        <template v-else>
          It looks at your current code and errors, points out bugs, and suggests an approach — it
          will <b>not</b> write the solution for you.
        </template>
      </p>

      <div class="flex items-center gap-1 text-xs">
        <span class="text-slate-400 dark:text-slate-500 mr-1">Reply in:</span>
        <button v-for="(label, lc) in LANGS" :key="lc" @click="setLang(lc)" type="button"
                class="px-2 py-0.5 rounded-lg border"
                :class="lang === lc
                  ? 'border-violet-400 bg-violet-100 text-violet-700 dark:bg-violet-500/20 dark:text-violet-200'
                  : 'border-slate-300 dark:border-slate-700 text-slate-500 dark:text-slate-400'">
          {{ label }}
        </button>
      </div>

      <textarea v-model="question" rows="2"
                :placeholder="liveMode ? 'Optional: ask something specific (e.g. “what does line 12 do?”)' : 'Optional: ask something specific (e.g. “why does test 3 fail?”)'"
                class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-xs"></textarea>
      <div class="flex flex-wrap gap-2">
        <button @click="ask()" :disabled="busy || (!code && !liveMode)"
                class="bg-violet-600 hover:bg-violet-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ busy ? `Thinking… ${elapsed}s` : liveMode ? 'Ask about my code' : 'Ask for a hint' }}
        </button>
        <button v-if="liveMode" @click="ask({ explain: true })" :disabled="busy || !teacherCode"
                class="border border-violet-300 dark:border-violet-500/40 text-violet-700 dark:text-violet-300 rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          ✨ Explain the teacher's code
        </button>
      </div>

      <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      <div v-if="reply" class="text-sm bg-slate-50 dark:bg-slate-800/60 rounded-lg p-3">
        <p v-if="level > 0 && !liveMode" class="text-[10px] uppercase tracking-wide text-violet-500 dark:text-violet-400 mb-1">
          Hint {{ level }} / 4 · {{ LEVEL_LABEL[level] }}
        </p>
        <MarkdownBlock :text="reply" />
        <span v-if="busy" class="inline-block w-1.5 h-4 bg-violet-400 animate-pulse align-middle ml-0.5"></span>
      </div>
      <p v-if="level >= 2 && !busy && !liveMode" class="text-[11px] text-slate-400 dark:text-slate-500">
        Ask again for a more detailed hint (the tutor still won't give the full solution).
        <button @click="startOver" class="text-violet-500 dark:text-violet-400 hover:underline ml-1">Start over</button>
      </p>
    </div>
  </div>
</template>
