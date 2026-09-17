<script setup>
import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import { api } from '../lib/api';
import MonacoEditor from '../components/MonacoEditor.vue';
import SplitPane from '../components/SplitPane.vue';
import { CODE_TEMPLATES } from '../lib/templates';

// A no-strings scratch editor: anyone can try C / C++, Run it, see output.
// Nothing is submitted, graded, saved to the server, or shared.
const LANG_KEY = 'beecoding.playground.lang';
const STDIN_KEY = 'beecoding.playground.stdin';
const codeKey = (l) => `beecoding.playground.code.${l}`;

const lang = ref('cpp');
const code = ref(CODE_TEMPLATES.cpp);
const stdin = ref('');
const running = ref(false);
const runOut = ref(null);
const error = ref('');
const runCooldown = ref(0);   // seconds left before another run is allowed
let runCooldownTimer = null;
function startRunCooldown(seconds) {
  runCooldown.value = Math.ceil(seconds);
  clearInterval(runCooldownTimer);
  runCooldownTimer = setInterval(() => {
    runCooldown.value -= 1;
    if (runCooldown.value <= 0) { runCooldown.value = 0; clearInterval(runCooldownTimer); }
  }, 1000);
}

function loadCode(l) {
  try {
    const saved = localStorage.getItem(codeKey(l));
    code.value = saved != null ? saved : (CODE_TEMPLATES[l] || '');
  } catch { code.value = CODE_TEMPLATES[l] || ''; }
}
function setLang(l) {
  if (l === lang.value) return;
  lang.value = l;
  try { localStorage.setItem(LANG_KEY, l); } catch { /* ignore */ }
  loadCode(l);
}
function resetTemplate() {
  code.value = CODE_TEMPLATES[lang.value] || '';
}

async function run() {
  if (runCooldown.value > 0) return;
  error.value = ''; running.value = true; runOut.value = null;
  try {
    runOut.value = await api.post('/api/run', { language: lang.value, code: code.value, stdin: stdin.value });
    startRunCooldown(5);
  } catch (e) { error.value = e.message; }
  finally { running.value = false; }
}

let saveTimer = null;
watch(code, () => {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try { localStorage.setItem(codeKey(lang.value), code.value); } catch { /* ignore */ }
  }, 400);
});
watch(stdin, () => {
  try { localStorage.setItem(STDIN_KEY, stdin.value); } catch { /* ignore */ }
});

onMounted(() => {
  try {
    const l = localStorage.getItem(LANG_KEY);
    if (l === 'c' || l === 'cpp') lang.value = l;
  } catch { /* ignore */ }
  try {
    const s = localStorage.getItem(STDIN_KEY);
    if (s != null) stdin.value = s;
  } catch { /* ignore */ }
  loadCode(lang.value);
});
onBeforeUnmount(() => { clearInterval(runCooldownTimer); });
</script>

<template>
  <div class="h-full flex flex-col">
    <div class="flex items-center gap-3 px-4 py-2 border-b border-slate-200 dark:border-slate-800 flex-wrap">
      <h1 class="text-sm font-bold">🧪 Playground</h1>
      <span class="text-xs text-slate-400 dark:text-slate-500">Scratch C / C++ — nothing is graded or shared.</span>
      <div class="ml-auto flex items-center gap-2">
        <button @click="resetTemplate"
                class="text-xs px-2.5 py-1 rounded-lg border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300">
          Reset template
        </button>
        <span class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
          <button v-for="l in ['c', 'cpp']" :key="l" @click="setLang(l)" class="px-2.5 py-1"
                  :class="lang === l ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
            {{ l === 'c' ? 'C' : 'C++' }}
          </button>
        </span>
      </div>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 px-4 py-2">{{ error }}</p>

    <div class="flex-1 min-h-0">
      <SplitPane direction="vertical" storage-key="beecoding.split.playground" :initial="68" :min="110">
        <template #a>
          <MonacoEditor v-model="code" :language="lang" :lsp="lang" />
        </template>
        <template #b>
          <div class="h-full overflow-y-auto bg-white dark:bg-slate-900 p-3 space-y-2">
            <div class="flex gap-2 items-center">
              <button @click="run" :disabled="running || runCooldown > 0"
                      class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
                {{ running ? 'Running…' : runCooldown > 0 ? `Wait ${runCooldown}s` : 'Run' }}
              </button>
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
          </div>
        </template>
      </SplitPane>
    </div>
  </div>
</template>
