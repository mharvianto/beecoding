<script setup>
import { ref, computed, watch, nextTick } from 'vue';
import MarkdownBlock from './MarkdownBlock.vue';
import { SUPPORTED_LANGS } from '../lib/templates';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import { docLang } from '../lib/docLang';
import TagSuggest from './TagSuggest.vue';

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
const emit = defineEmits(['save', 'delete', 'cancel', 'regenerate-tests', 'add-extreme-tests', 'dirty']);

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
function addTag(t) {
  const cur = form.value.tags.split(',').map((x) => x.trim()).filter(Boolean);
  if (!cur.some((x) => x.toLowerCase() === t.toLowerCase())) form.value.tags = [...cur, t].join(', ');
}
const tab = ref('problem');   // 'problem' | 'tests'
let saved = '';               // form as loaded/saved, to tell whether it changed

watch(() => props.problem, (p) => {
  form.value = p ? { ...blank(), ...JSON.parse(JSON.stringify(p)) } : blank();
  tab.value = 'problem';
  saved = JSON.stringify(form.value);
}, { immediate: true });

// Tell the page whether there is anything unsaved, so it can ask before the tab or route is left.
watch(form, (f) => emit('dirty', JSON.stringify(f) !== saved), { deep: true });

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

// Toolbar above the statement: each button edits the text around the cursor (or the selection).
// The edit goes through execCommand('insertText') so Ctrl+Z still undoes it, like typing; assigning the value would wipe the undo history.
function apply(start, end, text, selFrom, selTo) {
  const el = stmtEl.value;
  el.focus();
  el.setSelectionRange(start, end);
  if (!document.execCommand('insertText', false, text)) {   // not available: plain assignment (no undo, but never loses the edit)
    const v = form.value.statementMarkdown;
    form.value.statementMarkdown = v.slice(0, start) + text + v.slice(end);
  }
  nextTick(() => el.setSelectionRange(start + selFrom, start + selTo));
}
// Run an edit once the write pane is showing (v-show), with the current text and selection.
function edit(fn) {
  if (!stmtEl.value) return;
  showWrite.value = true;
  nextTick(() => { const el = stmtEl.value; fn(form.value.statementMarkdown, el.selectionStart, el.selectionEnd); });
}
const wrap = (before, after, placeholder) => edit((v, s, e) => {
  const sel = v.slice(s, e) || placeholder;
  apply(s, e, before + sel + after, before.length, before.length + sel.length);
});
/** Prefix every line touched by the selection: "- ", "1. " ... (a second click on a list button is not undone, use Ctrl+Z). */
const prefixLines = (prefixOf) => edit((v, s, e) => {
  const from = v.lastIndexOf('\n', s - 1) + 1;
  const nl = v.indexOf('\n', e);
  const to = nl === -1 ? v.length : nl;
  const lines = v.slice(from, to).split('\n').map((l, i) => prefixOf(i) + l);
  const text = lines.join('\n');
  apply(from, to, text, 0, text.length);
});
/** A block of its own: blank line before and after, so Markdown reads it as a paragraph / table. */
const insertBlock = (block, selFrom, selTo) => edit((v, s, e) => {
  const lead = s === 0 || v.slice(Math.max(0, s - 2), s) === '\n\n' ? '' : v[s - 1] === '\n' ? '\n' : '\n\n';
  const trail = v.slice(e, e + 2) === '\n\n' ? '' : v[e] === '\n' ? '\n' : '\n\n';
  apply(s, e, lead + block + trail, lead.length + selFrom, lead.length + selTo);
});
const TABLE = '| Column 1 | Column 2 |\n| --- | --- |\n| a | b |';
const insertTable = () => insertBlock(TABLE, 2, 10);

// Formulas: $...$ inside a sentence, $$...$$ on its own line. The list feeds both the menu and the help panel.
const FORMULAS = [
  { label: 'Constraint  1 ≤ n ≤ 2·10⁵', tex: '1 \\le n \\le 2 \\times 10^5', hint: '\\le ≤  \\ge ≥  \\ne ≠  \\times ×  \\cdot ·' },
  { label: 'Fraction', tex: '\\frac{a}{b}', hint: 'a over b' },
  { label: 'Square root', tex: '\\sqrt{x}', hint: 'other roots: \\sqrt[3]{x}' },
  { label: 'Power / subscript', tex: 'a_{i+1}^{10}', hint: '^ power, _ subscript; braces when it is more than one character' },
  { label: 'Modulo 10⁹+7', tex: '\\text{answer} \\bmod (10^9 + 7)', hint: 'also \\pmod{p}; \\text{...} writes words inside a formula' },
  { label: 'Floor / ceiling', tex: '\\lfloor n/2 \\rfloor + \\lceil n/2 \\rceil', hint: 'round down / round up' },
  { label: 'List  a₁, …, aₙ', tex: 'a_1, a_2, \\ldots, a_n', hint: '\\ldots gives the dots' },
  { label: 'Symbols', tex: '\\alpha + \\beta \\ge \\gamma,\\ \\forall x \\in \\mathbb{N}', hint: 'Greek letters, \\in, \\forall, \\mathbb{N}' },
  { label: 'Binomial', tex: '\\binom{n}{k}', hint: 'n choose k' },
  { label: 'Min / max', tex: '\\min_{1 \\le i \\le n} a_i', hint: '\\max, \\gcd and \\log work the same way' },
  { label: 'Sum (own line)', tex: '\\sum_{i=1}^{n} a_i', display: true, hint: 'a formula on its own line is centred and bigger' },
  { label: 'Cases (own line)', tex: 'f(n) = \\begin{cases} 1 & n = 0 \\\\ n \\cdot f(n-1) & n > 0 \\end{cases}', display: true, hint: '& separates the columns, \\\\ ends a row' },
  { label: 'Matrix (own line)', tex: '\\begin{pmatrix} 1 & 2 \\\\ 3 & 4 \\end{pmatrix}', display: true, hint: 'same & and \\\\ rules' },
];
const showFormulaHelp = ref(false);
const inlineFormula = () => wrap('$', '$', 'x^2');
const blockFormula = () => insertBlock('$$\\sum_{i=1}^{n} a_i$$', 2, 20);
function insertFormula(f) {
  if (!f || !stmtEl.value) return;
  showWrite.value = true;
  // A formula inserted a moment ago stays selected between its dollar signs: take those along, so the new one replaces it whole.
  const el = stmtEl.value, v = form.value.statementMarkdown;
  let s = el.selectionStart, end = el.selectionEnd;
  while (s > 0 && end < v.length && v[s - 1] === '$' && v[end] === '$' && s !== end) { s--; end++; if (end - s > 400) break; }
  el.setSelectionRange(s, end);
  if (f.display) insertBlock(`$$${f.tex}$$`, 2, 2 + f.tex.length);
  else edit((v, s, e) => apply(s, e, `$${f.tex}$`, 1, 1 + f.tex.length));   // replaces the selection: a pick adds a formula, it does not wrap one
}
function pickFormula(e) {
  const f = FORMULAS[e.target.value];
  e.target.value = '';   // back to the label, so the same one can be picked again
  insertFormula(f);
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
          <div v-show="showWrite" class="flex flex-wrap items-center gap-1 mb-1 text-xs" role="toolbar" aria-label="Formatting">
            <!-- mousedown.prevent: the box keeps its focus and selection while a button is pressed -->
            <button type="button" @mousedown.prevent @click="wrap('**', '**', 'bold')" class="tb font-bold" title="Bold (Ctrl+B)">B</button>
            <button type="button" @mousedown.prevent @click="wrap('*', '*', 'italic')" class="tb italic" title="Italic (Ctrl+I)">I</button>
            <button type="button" @mousedown.prevent @click="wrap('`', '`', 'code')" class="tb font-mono" title="Code">&lt;/&gt;</button>
            <span class="w-px h-4 bg-slate-300 dark:bg-slate-700 mx-1"></span>
            <button type="button" @mousedown.prevent @click="prefixLines(() => '- ')" class="tb" title="Bulleted list">• List</button>
            <button type="button" @mousedown.prevent @click="prefixLines((i) => `${i + 1}. `)" class="tb" title="Numbered list">1. List</button>
            <button type="button" @mousedown.prevent @click="insertTable" class="tb" title="Insert a table">▦ Table</button>
            <span class="w-px h-4 bg-slate-300 dark:bg-slate-700 mx-1"></span>
            <button type="button" @mousedown.prevent @click="inlineFormula" class="tb" title="Formula inside a sentence: $ ... $ (Ctrl+M)">$ x $</button>
            <button type="button" @mousedown.prevent @click="blockFormula" class="tb" title="Formula on its own line: $$ ... $$">$$ ∑ $$</button>
            <select @change="pickFormula" class="tb bg-transparent" title="Insert a common formula" aria-label="Insert a common formula">
              <option value="">Formula ▾</option>
              <option v-for="(f, i) in FORMULAS" :key="i" :value="i">{{ f.label }}</option>
            </select>
            <button type="button" @mousedown.prevent @click="showFormulaHelp = !showFormulaHelp" class="tb" :class="showFormulaHelp ? '!border-amber-400 !text-amber-700 dark:!text-amber-300' : ''"
                    title="Examples of formulas often used in problems" :aria-expanded="showFormulaHelp">? Formula help</button>
          </div>
          <div v-if="showWrite && showFormulaHelp" class="mb-2 border border-slate-300 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-900 text-xs">
            <div class="px-3 py-2 text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
              Type the code between dollar signs: <code v-pre>$...$</code> inside a sentence, <code v-pre>$$...$$</code> on its own line.
              Click <b>Insert</b> to put an example at the cursor. A plain dollar sign (<code v-pre>$5 and $10</code>) needs nothing special.
            </div>
            <div class="max-h-72 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
              <div v-for="(f, i) in FORMULAS" :key="i" class="grid grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)_auto] gap-3 items-center px-3 py-1.5">
                <div class="min-w-0 overflow-x-auto"><MarkdownBlock :text="(f.display ? '$$' : '$') + f.tex + (f.display ? '$$' : '$')" class="[&_p]:!my-0" /></div>
                <div class="min-w-0">
                  <code class="block font-mono text-[11px] text-slate-600 dark:text-slate-300 break-all">{{ f.tex }}</code>
                  <span class="text-[11px] text-slate-400 dark:text-slate-500">{{ f.label }} · {{ f.hint }}</span>
                </div>
                <button type="button" @mousedown.prevent @click="insertFormula(f)" class="tb">Insert</button>
              </div>
            </div>
          </div>
          <div class="flex flex-col md:flex-row gap-2">
            <textarea ref="stmtEl" v-show="showWrite" v-model="form.statementMarkdown" placeholder="Statement (Markdown)" rows="12"
                      @keydown.ctrl.b.prevent="wrap('**', '**', 'bold')" @keydown.meta.b.prevent="wrap('**', '**', 'bold')"
                      @keydown.ctrl.i.prevent="wrap('*', '*', 'italic')" @keydown.meta.i.prevent="wrap('*', '*', 'italic')"
                      @keydown.ctrl.m.prevent="inlineFormula" @keydown.meta.m.prevent="inlineFormula"
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
          <TagSuggest class="w-full" :title="form.title" :statement="form.statementMarkdown" :current="form.tags" @add="addTag" />
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
