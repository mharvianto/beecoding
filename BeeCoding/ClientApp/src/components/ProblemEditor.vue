<script setup>
import { ref, computed, watch } from 'vue';
import MarkdownBlock from './MarkdownBlock.vue';
import { SUPPORTED_LANGS } from '../lib/templates';

// statement: editor + preview side by side, either pane hideable (never both)
const showWrite = ref(true);
const showPreview = ref(true);
function toggleWrite() { if (showPreview.value || !showWrite.value) showWrite.value = !showWrite.value; }
function togglePreview() { if (showWrite.value || !showPreview.value) showPreview.value = !showPreview.value; }

/**
 * Form for a problem. API-agnostic: the parent decides where `save` writes
 * (a board's problems, or the teacher's bank).
 */
const props = defineProps({
  problem: Object,                 // null => new
  showBankFields: Boolean,         // tags + public toggle (bank only)
  error: String,
  busy: Boolean,
  canRegenTests: Boolean,          // show "regenerate tests with AI" (bank, existing, AI on)
  regenBusy: Boolean,
});
const emit = defineEmits(['save', 'delete', 'cancel', 'regenerate-tests']);

const blank = () => ({
  title: '', statementMarkdown: '', allowedLanguages: '',
  timeLimitMs: 1000, memoryLimitKb: 32768, position: 0,
  level: 'Medium', tags: '', isPublic: false, bannedHeaders: '', bannedSymbols: '', inputFileName: '', testCases: [],
});
const form = ref(blank());
const tab = ref('problem');   // 'problem' | 'tests'

watch(() => props.problem, (p) => {
  form.value = p ? { ...blank(), ...JSON.parse(JSON.stringify(p)) } : blank();
  tab.value = 'problem';
}, { immediate: true });

// allowed languages as a checkbox array <-> csv on the form (empty csv = all allowed)
const allowedLangsArr = computed({
  get: () => (form.value.allowedLanguages || '').split(',').map((s) => s.trim()).filter(Boolean),
  set: (arr) => {
    form.value.allowedLanguages = SUPPORTED_LANGS.filter((l) => arr.includes(l)).join(',');
  },
});

function addTest() {
  form.value.testCases.push({ id: null, stdin: '', expectedStdout: '', isSample: false, points: 1, position: form.value.testCases.length });
}
function removeTest(i) { form.value.testCases.splice(i, 1); }
</script>

<template>
  <div class="text-slate-900 dark:text-slate-100">
    <div class="max-w-5xl mx-auto px-4 py-6">
      <div class="flex items-center gap-3 flex-wrap mb-4">
        <h2 class="font-bold text-lg">{{ props.problem?.id ? 'Edit' : 'New' }} problem</h2>
        <span v-if="props.problem?.generatedByAi"
              class="text-[11px] px-1.5 py-0.5 rounded-full bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">✨ AI-generated</span>
        <span v-if="props.problem?.pendingReview"
              class="text-[11px] px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300"
              title="An admin reviews AI-generated problems before they're shared with other teachers">
          ⏳ pending review — not shared yet
        </span>
        <div class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-sm">
          <button type="button" @click="tab = 'problem'" class="px-3 py-1"
                  :class="tab === 'problem' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">Problem</button>
          <button type="button" @click="tab = 'tests'" class="px-3 py-1"
                  :class="tab === 'tests' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
            Test cases · {{ form.testCases.length }}
          </button>
        </div>
      </div>

      <div v-show="tab === 'problem'" class="space-y-3">
        <input v-model="form.title" placeholder="Title" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <div>
          <div class="flex items-center gap-2 text-xs mb-1">
            <span class="text-slate-400 dark:text-slate-500">Statement (Markdown)</span>
            <button type="button" @click="toggleWrite"
                    class="ml-auto px-2 py-0.5 rounded border"
                    :class="showWrite ? 'border-amber-400 text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-500/10' : 'border-slate-300 dark:border-slate-700 text-slate-400 dark:text-slate-500'">✎ Write</button>
            <button type="button" @click="togglePreview"
                    class="px-2 py-0.5 rounded border"
                    :class="showPreview ? 'border-amber-400 text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-500/10' : 'border-slate-300 dark:border-slate-700 text-slate-400 dark:text-slate-500'">👁 Preview</button>
          </div>
          <div class="flex flex-col md:flex-row gap-2">
            <textarea v-show="showWrite" v-model="form.statementMarkdown" placeholder="Statement (Markdown)" rows="12"
                      class="flex-1 min-w-0 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 font-mono text-sm"></textarea>
            <div v-show="showPreview"
                 class="flex-1 min-w-0 h-[19rem] overflow-y-auto border border-slate-300 dark:border-slate-700 rounded-lg px-3 py-2 bg-slate-50 dark:bg-slate-800/40">
              <MarkdownBlock v-if="form.statementMarkdown.trim()" :text="form.statementMarkdown" />
              <p v-else class="text-sm text-slate-400 dark:text-slate-500">Nothing to preview yet.</p>
            </div>
          </div>
        </div>
        <div class="flex gap-3 flex-wrap text-sm items-center">
          <span class="flex items-center gap-2">Allowed languages
            <label class="flex items-center gap-1"><input type="checkbox" value="c" v-model="allowedLangsArr" /> C</label>
            <label class="flex items-center gap-1"><input type="checkbox" value="cpp" v-model="allowedLangsArr" /> C++</label>
            <span class="text-[11px] text-slate-400 dark:text-slate-500">none checked = all languages</span>
          </span>
          <label class="flex items-center gap-1">Level
            <select v-model="form.level" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1">
              <option>Easy</option><option>Medium</option><option>Hard</option>
            </select>
          </label>
          <label class="flex items-center gap-1">Time (ms)
            <input v-model.number="form.timeLimitMs" type="number" class="w-24 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label class="flex items-center gap-1">Memory (KB)
            <input v-model.number="form.memoryLimitKb" type="number" class="w-28 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label v-if="!showBankFields" class="flex items-center gap-1">Position
            <input v-model.number="form.position" type="number" class="w-16 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
        </div>

        <div class="space-y-2">
          <label class="flex items-center gap-1 text-sm">Banned headers
            <input v-model="form.bannedHeaders" placeholder="e.g. algorithm, numeric"
                   class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label class="flex items-center gap-1 text-sm">Banned functions
            <input v-model="form.bannedSymbols" placeholder="e.g. std::sort, qsort, stable_sort"
                   class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <p class="text-[11px] text-slate-400 dark:text-slate-500">
            Comma-separated. <b>Headers</b>: names a submission may not <code>#include</code> (setting any also blocks
            <code>&lt;bits/stdc++.h&gt;</code>). <b>Functions</b>: identifiers it may not use — <code>sort</code> also
            catches <code>std::sort</code>; write <code>std::sort</code> to match only the qualified call. A violation
            is reported as a Compile Error, on Run and Submit.
          </p>
        </div>

        <div>
          <label class="flex items-center gap-1 text-sm">Input file (optional)
            <input v-model="form.inputFileName" placeholder="e.g. input.txt — leave blank to read from stdin"
                   class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <p class="text-[11px] text-slate-400 dark:text-slate-500">
            When set, each test's input is written to this filename in the program's working directory instead of
            being piped to stdin — for problems that require <code>fopen()</code>-style file I/O. Leave blank for
            the normal stdin/stdout behavior.
          </p>
        </div>

        <div class="flex gap-3 flex-wrap text-sm items-center">
          <label class="flex items-center gap-1 flex-1 min-w-48">Tags
            <input v-model="form.tags" placeholder="loop, array, dp"
                   class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label v-if="showBankFields" class="flex items-center gap-2" :class="{ 'opacity-50': props.problem?.pendingReview }">
            <input type="checkbox" v-model="form.isPublic" :disabled="props.problem?.pendingReview" />
            Share with other teachers
            <span v-if="props.problem?.pendingReview" class="text-[11px] text-slate-400">(awaiting admin review)</span>
          </label>
        </div>

      </div>

      <div v-show="tab === 'tests'">
        <div class="flex items-center gap-3 mb-1 flex-wrap">
          <h3 class="font-semibold text-sm">Test cases</h3>
          <button v-if="canRegenTests" @click="emit('regenerate-tests')" :disabled="regenBusy"
                  class="text-xs text-violet-600 dark:text-violet-400 disabled:opacity-50">
            {{ regenBusy ? '✨ regenerating…' : '✨ regenerate hidden tests' }}
          </button>
          <button @click="addTest" class="text-xs text-amber-600 ml-auto">+ add test</button>
        </div>
        <p v-if="canRegenTests" class="text-[11px] text-slate-400 dark:text-slate-500 mb-2">
          Keeps your statement &amp; samples; the AI writes a fresh reference solution
          (checked against your samples) and a new, diverse set of hidden tests.
        </p>
        <div v-for="(t, i) in form.testCases" :key="i" class="border border-slate-200 dark:border-slate-800 rounded-lg p-2 mb-2">
          <div class="grid grid-cols-2 gap-2">
            <textarea v-model="t.stdin" placeholder="stdin" rows="2"
                      class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 font-mono text-xs"></textarea>
            <textarea v-model="t.expectedStdout" placeholder="expected stdout" rows="2"
                      class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 font-mono text-xs"></textarea>
          </div>
          <div class="flex items-center gap-3 text-xs mt-1">
            <label class="flex items-center gap-1"><input type="checkbox" v-model="t.isSample" /> sample (visible to students)</label>
            <label class="flex items-center gap-1">points <input v-model.number="t.points" type="number" class="w-14 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-1" /></label>
            <button @click="removeTest(i)" class="text-red-500 ml-auto">remove</button>
          </div>
        </div>
        <p v-if="!form.testCases.length" class="text-sm text-slate-400 dark:text-slate-500">No test cases yet — add one, or generate with AI.</p>
      </div>

      <pre v-if="error" class="mt-4 text-xs text-red-600 dark:text-red-400 whitespace-pre-wrap max-h-52 overflow-auto border border-red-200 dark:border-red-500/30 rounded-lg p-3">{{ error }}</pre>

      <div class="mt-6 pt-4 border-t border-slate-200 dark:border-slate-800 flex items-center gap-2">
        <button @click="emit('save', form)" :disabled="busy"
                class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 font-medium disabled:opacity-50">
          {{ busy ? 'Saving…' : 'Save' }}
        </button>
        <button @click="emit('cancel')" class="text-slate-500 dark:text-slate-400 px-3">Cancel</button>
        <button v-if="props.problem?.id" @click="emit('delete')" class="text-red-600 dark:text-red-400 ml-auto px-3">Delete</button>
      </div>
    </div>
  </div>
</template>
