<script setup>
import { ref, computed, watch } from 'vue';
import MarkdownBlock from './MarkdownBlock.vue';
import { SUPPORTED_LANGS } from '../lib/templates';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import { docLang } from '../lib/docLang';

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
  studentMode: Boolean,            // a student's bank draft: no direct sharing (it goes through review)
  error: String,
  busy: Boolean,
  canRegenTests: Boolean,          // show "regenerate tests with AI" (bank, existing, AI on)
  regenBusy: Boolean,
  addExtremeBusy: Boolean,         // "add extreme tests" shares the same canRegenTests gate
});
const emit = defineEmits(['save', 'delete', 'cancel', 'regenerate-tests', 'add-extreme-tests']);

// What a new problem starts with: the house layout. The sample tests are listed under the statement automatically,
// so there is no "Contoh" section to write. A picture is added with the "Add image" button above the box.
const STATEMENT_TEMPLATES = {
  id: `Tulis ceritanya atau masalahnya di sini, singkat dan jelas.

**Format Input:** jelaskan baris demi baris apa yang dibaca program.

**Format Output:** jelaskan apa yang harus dicetak, termasuk format angka atau teksnya.

**Batasan:** 1 <= N <= 1000.
`,
  en: `Tell the story or describe the task here, briefly and clearly.

**Input Format:** explain, line by line, what the program reads.

**Output Format:** explain what must be printed, including the number or text format.

**Constraints:** 1 <= N <= 1000.
`,
};
// follows the language chosen on the guide / terms pages (English unless switched), so the guide shows what the editor gives
const statementTemplate = () => STATEMENT_TEMPLATES[docLang.value] ?? STATEMENT_TEMPLATES.en;

const blank = () => ({
  title: '', statementMarkdown: statementTemplate(), allowedLanguages: '',
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
function removeTest(i) { form.value.testCases.splice(i, 1); renumber(); }

// Order matters: the judge runs tests by Position (and reports the first failing one), and samples are listed in that order.
function renumber() { form.value.testCases.forEach((t, i) => { t.position = i; }); }
function moveTest(from, to) {
  const list = form.value.testCases;
  if (from === to || to < 0 || to >= list.length) return;
  list.splice(to, 0, list.splice(from, 1)[0]);
  renumber();
}
// A stable key per row, so a reordered row keeps its own textareas (index keys would swap the DOM under the dragged row).
const rowKeys = new WeakMap();
let rowSeq = 0;
const rowKey = (t) => rowKeys.get(t) ?? (rowKeys.set(t, ++rowSeq), rowSeq);
// Drag with the ⠿ handle (the textareas stay selectable); ▲ ▼ do the same by click or touch.
const dragFrom = ref(null);
const dragOver = ref(null);
function onDragStart(e, i) {
  dragFrom.value = i;
  e.dataTransfer.effectAllowed = 'move';
  e.dataTransfer.setData('text/plain', String(i));
  const card = e.currentTarget.closest('[data-test-row]');
  if (card) e.dataTransfer.setDragImage(card, 12, 12);
}
function onDrop(i) { if (dragFrom.value !== null) moveTest(dragFrom.value, i); dragFrom.value = dragOver.value = null; }
function onDragEnd() { dragFrom.value = dragOver.value = null; }
function save() { renumber(); emit('save', form); }

// Pictures: upload, then drop the Markdown for it at the cursor.
const stmtEl = ref(null);
const imgBusy = ref(false);
const imgError = ref('');
async function addImage(e) {
  const file = e.target.files?.[0];
  e.target.value = '';   // allow picking the same file again
  if (!file) return;
  imgBusy.value = true; imgError.value = '';
  try {
    const { url } = await api.upload('/api/images', file);
    const md = `![](${url})`;
    const text = form.value.statementMarkdown;
    const at = stmtEl.value?.selectionStart ?? text.length;
    form.value.statementMarkdown = text.slice(0, at) + md + text.slice(at);
    showWrite.value = true;
  } catch (err) { imgError.value = err.message; }
  finally { imgBusy.value = false; }
}
</script>

<template>
  <div class="text-slate-900 dark:text-slate-100">
    <div class="max-w-7xl mx-auto px-4 py-6">
      <div class="flex items-center gap-3 flex-wrap mb-4">
        <h2 class="font-bold text-lg">{{ props.problem?.id ? 'Edit' : 'New' }} problem</h2>
        <a :href="withBase('/guide/problems')" target="_blank" rel="noopener"
           class="text-xs text-amber-600 dark:text-amber-400 hover:underline">📖 Guide to writing problems</a>
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
            <label class="px-2 py-0.5 rounded border border-slate-300 dark:border-slate-700 text-slate-500 dark:text-slate-400 cursor-pointer"
                   :class="{ 'opacity-50 pointer-events-none': imgBusy }" title="Upload a PNG, JPEG, GIF or WebP (the admin sets the size limit; an accepted file is then compressed) and insert it at the cursor">
              {{ imgBusy ? 'Uploading…' : '🖼 Add image' }}
              <input type="file" accept="image/png,image/jpeg,image/gif,image/webp" class="hidden" @change="addImage" />
            </label>
            <button type="button" @click="toggleWrite"
                    class="ml-auto px-2 py-0.5 rounded border"
                    :class="showWrite ? 'border-amber-400 text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-500/10' : 'border-slate-300 dark:border-slate-700 text-slate-400 dark:text-slate-500'">✎ Write</button>
            <button type="button" @click="togglePreview"
                    class="px-2 py-0.5 rounded border"
                    :class="showPreview ? 'border-amber-400 text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-500/10' : 'border-slate-300 dark:border-slate-700 text-slate-400 dark:text-slate-500'">👁 Preview</button>
          </div>
          <p v-if="imgError" class="text-xs text-red-600 dark:text-red-400 mb-1">{{ imgError }}</p>
          <div class="flex flex-col md:flex-row gap-2">
            <textarea ref="stmtEl" v-show="showWrite" v-model="form.statementMarkdown" placeholder="Statement (Markdown)" rows="12"
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
          <label v-if="showBankFields && !studentMode" class="flex items-center gap-2" :class="{ 'opacity-50': props.problem?.pendingReview }">
            <input type="checkbox" v-model="form.isPublic" :disabled="props.problem?.pendingReview" />
            Share with other teachers
            <span v-if="props.problem?.pendingReview" class="text-[11px] text-slate-400">(awaiting admin review)</span>
          </label>
        </div>

      </div>

      <div v-show="tab === 'tests'">
        <div class="flex items-center gap-3 mb-1 flex-wrap">
          <h3 class="font-semibold text-sm">Test cases</h3>
          <button v-if="canRegenTests" @click="emit('regenerate-tests')" :disabled="regenBusy || addExtremeBusy"
                  class="text-xs text-violet-600 dark:text-violet-400 disabled:opacity-50">
            {{ regenBusy ? '✨ regenerating…' : '✨ regenerate hidden tests' }}
          </button>
          <button v-if="canRegenTests" @click="emit('add-extreme-tests')" :disabled="regenBusy || addExtremeBusy"
                  class="text-xs text-violet-600 dark:text-violet-400 disabled:opacity-50">
            {{ addExtremeBusy ? '✨ adding…' : '✨ add extreme tests' }}
          </button>
          <button @click="addTest" class="text-xs text-amber-600 ml-auto">+ add test</button>
        </div>
        <p v-if="canRegenTests" class="text-[11px] text-slate-400 dark:text-slate-500 mb-2">
          Regenerate keeps your statement &amp; samples but replaces the hidden tests with a
          fresh, diverse set. Add extreme tests keeps every existing test and appends a few
          new boundary cases (min/max size, min/max value) at the end.
        </p>
        <div v-for="(t, i) in form.testCases" :key="rowKey(t)" data-test-row
             @dragover.prevent="dragOver = i" @drop.prevent="onDrop(i)"
             class="border rounded-lg p-2 mb-2"
             :class="[dragOver === i && dragFrom !== null && dragFrom !== i ? 'border-amber-400 bg-amber-50/40 dark:bg-amber-500/5' : 'border-slate-200 dark:border-slate-800',
                      dragFrom === i ? 'opacity-50' : '']">
          <div class="flex items-center gap-1 text-xs text-slate-400 dark:text-slate-500 mb-1">
            <span draggable="true" @dragstart="onDragStart($event, i)" @dragend="onDragEnd"
                  class="cursor-grab active:cursor-grabbing select-none px-1 text-base leading-none" title="Drag to reorder">⠿</span>
            <span>#{{ i + 1 }}</span>
            <button type="button" @click="moveTest(i, i - 1)" :disabled="i === 0" title="Move up" aria-label="Move test up"
                    class="px-1.5 hover:text-slate-700 dark:hover:text-slate-200 disabled:opacity-30">▲</button>
            <button type="button" @click="moveTest(i, i + 1)" :disabled="i === form.testCases.length - 1" title="Move down" aria-label="Move test down"
                    class="px-1.5 hover:text-slate-700 dark:hover:text-slate-200 disabled:opacity-30">▼</button>
          </div>
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
        <button @click="save" :disabled="busy"
                class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 font-medium disabled:opacity-50">
          {{ busy ? 'Saving…' : 'Save' }}
        </button>
        <button @click="emit('cancel')" class="text-slate-500 dark:text-slate-400 px-3">Cancel</button>
        <button v-if="props.problem?.id" @click="emit('delete')" class="text-red-600 dark:text-red-400 ml-auto px-3">Delete</button>
      </div>
    </div>
  </div>
</template>
