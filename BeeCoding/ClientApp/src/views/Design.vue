<script setup>
import { vModal } from '../lib/vModal';
import { ref, computed } from 'vue';
import Icon from '../components/Icon.vue';
import iconSource from '../components/Icon.vue?raw';
import Chevron from '../components/Chevron.vue';
import VerdictBadge from '../components/VerdictBadge.vue';
import LevelBadge from '../components/LevelBadge.vue';
import RowMenu from '../components/RowMenu.vue';
import { theme, setTheme } from '../lib/theme';
import { useConfirmDialog } from '../stores/confirmDialog';

// Living style guide: every sample is the real component or the real class from DESIGN.md, so it cannot drift from the app.
// Icon names are read from Icon.vue itself, so a new icon shows up here without touching this file.
const icons = [...iconSource.matchAll(/^\s{2}(\w+):\s*'/gm)].map((m) => m[1]);
const confirm = useConfirmDialog();
const seg = ref('a');
const open = ref(true);
const modal = ref(false);
const note = ref('');

const swatches = [
  ['Page', 'bg-[#f8fafc] dark:bg-[#0b1120]'], ['Card', 'bg-white dark:bg-slate-900'], ['Card border', 'bg-slate-200 dark:bg-slate-800'],
  ['Text secondary', 'bg-slate-500 dark:bg-slate-400'], ['Accent / primary', 'bg-amber-500'], ['Secondary active', 'bg-violet-600 dark:bg-violet-400'],
  ['Success', 'bg-emerald-600 dark:bg-emerald-400'], ['Danger', 'bg-rose-600 dark:bg-rose-400'], ['Error text', 'bg-red-600 dark:bg-red-400'],
];
const dataColors = ['#f59e0b', '#0ea5e9', '#10b981', '#8b5cf6', '#f43f5e', '#64748b'];
const verdicts = ['Accepted', 'WrongAnswer', 'TimeLimit', 'MemoryLimit', 'RuntimeError', 'CompileError', 'Queued', 'Running', 'Hidden', 'None'];
const section = 'font-semibold text-sm uppercase tracking-wide text-amber-700 dark:text-amber-400 mb-3';
const card = 'rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-4';
const segOn = 'bg-slate-800 text-white dark:bg-slate-600';
const segOff = 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60';
const themeNow = computed(() => theme.value);
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8 space-y-10">
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 class="text-2xl font-bold">Design components</h1>
        <p class="text-sm text-slate-500 dark:text-slate-400 mt-1">The rules are in <code class="font-mono text-xs">DESIGN.md</code>. Everything here is the real component or class.</p>
      </div>
      <div class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs" role="group" aria-label="Theme">
        <button v-for="t in ['light', 'dark', 'system']" :key="t" type="button" @click="setTheme(t)" :aria-pressed="themeNow === t"
                class="px-3 py-1 capitalize" :class="themeNow === t ? segOn : segOff">{{ t }}</button>
      </div>
    </header>

    <section>
      <h2 :class="section">Colour</h2>
      <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
        <div v-for="[name, cls] in swatches" :key="name" :class="card" class="flex items-center gap-3 !p-3">
          <span class="w-8 h-8 rounded-lg border border-slate-200 dark:border-slate-700 shrink-0" :class="cls"></span>
          <span class="text-xs"><span class="font-medium block">{{ name }}</span><span class="font-mono text-[11px] text-slate-500 dark:text-slate-400 break-all">{{ cls }}</span></span>
        </div>
      </div>
      <p class="text-xs text-slate-500 dark:text-slate-400 mt-3 mb-1.5">Chart data colours, in this order</p>
      <div class="flex gap-2"><span v-for="c in dataColors" :key="c" class="w-8 h-8 rounded-lg" :style="{ background: c }" :title="c"></span></div>
    </section>

    <section>
      <h2 :class="section">Typography</h2>
      <div :class="card" class="space-y-2">
        <p class="text-2xl font-bold">Page title <span class="text-xs font-normal text-slate-500">text-2xl font-bold</span></p>
        <p class="text-xl font-bold">Page title <span class="text-xs font-normal text-slate-500">text-xl font-bold</span></p>
        <p class="font-semibold text-sm">Section title <span class="text-xs font-normal text-slate-500">font-semibold text-sm</span></p>
        <p class="text-sm">Body text <span class="text-xs text-slate-500">text-sm</span></p>
        <p class="text-xs text-slate-500 dark:text-slate-400">Secondary text, text-xs slate-500 / slate-400</p>
        <p class="text-[11px] text-slate-500 dark:text-slate-400">Smallest allowed, text-[11px]: hints, badges, times</p>
        <p class="font-mono text-sm">int main(void) { return 0; } <span class="font-sans text-xs text-slate-500">font-mono</span></p>
      </div>
    </section>

    <section>
      <h2 :class="section">Buttons</h2>
      <div :class="card" class="space-y-4">
        <div class="flex flex-wrap items-center gap-3">
          <button type="button" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">Primary</button>
          <button type="button" disabled class="bg-amber-500 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">Primary, disabled</button>
          <button type="button" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-3 py-1 text-xs font-medium">Primary, in a dense row</button>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <button type="button" class="row-action-btn"><Icon name="pencil" /><span>Edit</span></button>
          <button type="button" class="row-action-btn row-action-btn--accent"><Icon name="eye" /><span>Accent</span></button>
          <button type="button" class="row-action-btn row-action-btn--success"><Icon name="check" /><span>Success</span></button>
          <button type="button" class="row-action-btn row-action-btn--danger"><Icon name="trash" /><span>Delete</span></button>
          <RowMenu><button type="button" class="row-menu-item">Rename</button><div class="row-menu-sep"></div><button type="button" class="row-menu-item row-menu-item--danger">Delete</button></RowMenu>
          <button type="button" class="tb">Toolbar</button>
        </div>
        <p class="text-[11px] text-slate-500 dark:text-slate-400">One filled primary per screen or row. Everything else is a ghost <code class="font-mono">.row-action-btn</code>.</p>
      </div>
    </section>

    <section>
      <h2 :class="section">Inputs and toggles</h2>
      <div :class="card" class="space-y-4">
        <div class="grid sm:grid-cols-2 gap-3">
          <label class="block text-sm"><span class="block font-medium mb-1">Text</span>
            <input v-model="note" placeholder="Type here" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" /></label>
          <label class="block text-sm"><span class="block font-medium mb-1">Drop-down</span>
            <select class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5"><option>Easy</option><option>Medium</option></select></label>
        </div>
        <label class="block text-sm"><span class="block font-medium mb-1">Text area</span>
          <textarea rows="2" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm"></textarea></label>
        <div class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs" role="group" aria-label="Segmented">
          <button v-for="o in ['a', 'b', 'c']" :key="o" type="button" @click="seg = o" :aria-pressed="seg === o" class="px-3 py-1" :class="seg === o ? segOn : segOff">Option {{ o }}</button>
        </div>
      </div>
    </section>

    <section>
      <h2 :class="section">Badges</h2>
      <div :class="card" class="space-y-3">
        <div class="flex flex-wrap gap-2"><VerdictBadge v-for="v in verdicts" :key="v" :verdict="v" /></div>
        <div class="flex flex-wrap gap-2"><VerdictBadge v-for="v in verdicts.slice(0, 6)" :key="v" :verdict="v" small /></div>
        <div class="flex flex-wrap gap-2"><LevelBadge level="Easy" /><LevelBadge level="Medium" /><LevelBadge level="Hard" /></div>
      </div>
    </section>

    <section>
      <h2 :class="section">Links, states and chevron</h2>
      <div :class="card" class="space-y-3 text-sm">
        <p>Table link: <a href="#" @click.prevent class="hover:underline hover:text-amber-600 dark:hover:text-amber-400">Problem title</a> (plain text, amber on hover)</p>
        <p>Inline link: <a href="#" @click.prevent class="text-amber-600 dark:text-amber-300">Practice a problem</a></p>
        <p class="text-slate-500 dark:text-slate-400">Empty: nothing here yet. <a href="#" @click.prevent class="text-amber-600">Do the next step</a></p>
        <p role="alert" class="text-red-600 dark:text-red-400">Error: something went wrong.</p>
        <p role="status" class="text-emerald-700 dark:text-emerald-300">Saved.</p>
        <button type="button" @click="open = !open" :aria-expanded="open" class="inline-flex items-center gap-1 text-sm font-medium"><Chevron :open="open" />Collapsible header</button>
      </div>
    </section>

    <section>
      <h2 :class="section">List rows and cards</h2>
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800">
        <div v-for="r in [['Accepted', 'Binary Search', 'Medium', '12m ago'], ['WrongAnswer', 'Balik Array', 'Easy', '2 hours ago']]" :key="r[1]"
             class="px-4 py-2.5 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800/50 flex items-center gap-3">
          <VerdictBadge :verdict="r[0]" small /><span class="text-sm flex-1 truncate">{{ r[1] }}</span><LevelBadge :level="r[2]" />
          <span class="text-[11px] text-slate-500 dark:text-slate-400 tabular-nums">{{ r[3] }}</span>
        </div>
      </div>
      <div :class="card" class="mt-3 text-sm">Card: <code class="font-mono text-xs">rounded-xl border bg-white dark:bg-slate-900 p-4</code></div>
    </section>

    <section>
      <h2 :class="section">Dialogs</h2>
      <div :class="card" class="flex flex-wrap gap-3">
        <button type="button" @click="modal = true" class="row-action-btn row-action-btn--accent">Open large modal</button>
        <button type="button" @click="confirm.ask('Delete this item? This is the shared confirm dialog.', { confirmLabel: 'Delete' })" class="row-action-btn row-action-btn--danger">Confirm dialog (danger)</button>
        <button type="button" @click="confirm.ask('Replace the code in the editor?', { danger: false, confirmLabel: 'Replace' })" class="row-action-btn">Confirm dialog (normal)</button>
      </div>
    </section>

    <section>
      <h2 :class="section">Icons ({{ icons.length }})</h2>
      <div class="grid grid-cols-3 sm:grid-cols-5 md:grid-cols-8 gap-2">
        <div v-for="n in icons" :key="n" :class="card" class="!p-2 flex flex-col items-center gap-1 text-slate-600 dark:text-slate-300">
          <span class="text-xl leading-none"><Icon :name="n" /></span><span class="text-[11px] text-slate-500 dark:text-slate-400 truncate max-w-full">{{ n }}</span>
        </div>
      </div>
    </section>

    <div v-if="modal" role="dialog" aria-modal="true" aria-label="Example" v-modal="() => (modal = false)" @click.self="modal = false"
         class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50">
      <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-3xl my-8 flex flex-col overflow-hidden" style="height: 60vh">
        <div class="border-b border-slate-100 dark:border-slate-800 shrink-0">
          <div class="flex items-center justify-between gap-2 px-4 pt-3">
            <div class="flex items-center gap-2"><VerdictBadge verdict="Accepted" small /><span class="text-sm font-semibold">Title (2 hours ago)</span></div>
            <button type="button" @click="modal = false" aria-label="Close" title="Close" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200">✕</button>
          </div>
          <div class="flex items-center gap-2 flex-wrap px-4 pb-2.5 pt-1 text-xs text-slate-500 dark:text-slate-400">
            <span>0ms · 1408KB · 100%</span>
            <button type="button" class="row-action-btn"><span>⇄</span><span>Ghost action</span></button>
            <button type="button" class="shrink-0 ml-auto text-xs font-medium bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-3 py-1">Primary</button>
          </div>
        </div>
        <div class="flex-1 min-h-0 p-4 text-sm text-slate-500 dark:text-slate-400">Content fills the rest, without padding for editors.</div>
      </div>
    </div>
  </div>
</template>
