<script setup>
import { vModal } from '../lib/vModal';
import { ref, computed, nextTick, watch, onBeforeUnmount } from 'vue';
import { tr } from '../lib/i18n';
import { runText } from '../lib/runText';
import Icon from './Icon.vue';

// The "output" box under the editor, with a button that opens the same text in a big modal: easier to read a long output,
// copy it, and run again without closing it. Used by Solve, PracticeSolve, Playground and LiveCode.
const props = defineProps({ result: { type: Object, default: null }, fontSize: { type: Number, default: 13 }, running: Boolean });
const emit = defineEmits(['run']);

const text = computed(() => runText(props.result));
const open = ref(false);
const wrap = ref(true);
const copied = ref(false);
const closeBtn = ref(null);

// Esc is caught on the document: the focused Run button turns disabled while it runs, and a disabled element no longer gets key events.
const onKey = (e) => { if (e.key === 'Escape') open.value = false; };
watch(open, (v) => { if (v) document.addEventListener('keydown', onKey); else document.removeEventListener('keydown', onKey); });
onBeforeUnmount(() => document.removeEventListener('keydown', onKey));

function show() { open.value = true; nextTick(() => closeBtn.value?.focus()); }
async function copy() {
  try { await navigator.clipboard.writeText(text.value); copied.value = true; setTimeout(() => { copied.value = false; }, 1500); }
  catch { /* the browser refused (no permission or an insecure page): nothing to do */ }
}
</script>

<template>
  <div>
    <div class="flex items-center justify-between">
      <label class="text-xs text-slate-500 dark:text-slate-400">{{ tr('output') }}</label>
      <button type="button" @click="show" :title="tr('Open the output in a large window')" :aria-label="tr('Open the output in a large window')"
              class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 text-sm leading-none px-1"><Icon name="maximize" /></button>
    </div>
    <pre :style="{ fontSize: fontSize + 'px' }"
         class="w-full h-20 resize-y bg-slate-900 text-slate-100 dark:bg-black dark:border dark:border-slate-800 rounded-lg px-2 py-1 font-mono overflow-auto whitespace-pre-wrap">{{ text }}</pre>

    <Teleport to="body">
      <div v-if="open" role="dialog" aria-modal="true" v-modal="() => (open = false)" :aria-label="tr('output')" @click.self="open = false"
           class="fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50">
        <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-5xl my-8 flex flex-col overflow-hidden" style="height: 85vh">
          <div class="border-b border-slate-100 dark:border-slate-800 shrink-0">
            <div class="flex items-center justify-between gap-2 px-4 pt-3">
              <span class="text-sm font-semibold capitalize">{{ tr('output') }}</span>
              <button ref="closeBtn" type="button" @click="open = false" :aria-label="tr('Close')" :title="tr('Close')" class="text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200">✕</button>
            </div>
            <div class="flex items-center gap-2 flex-wrap px-4 pb-2.5 pt-1 text-xs">
              <button type="button" @click="copy" class="row-action-btn shrink-0"><Icon name="copy" /><span>{{ copied ? tr('Copied') : tr('Copy') }}</span></button>
              <button type="button" @click="wrap = !wrap" :aria-pressed="wrap" class="row-action-btn shrink-0" :class="wrap && 'row-action-btn--accent'"><Icon name="wrap" /><span>{{ tr('Wrap lines') }}</span></button>
              <button type="button" @click="emit('run')" :disabled="running"
                      class="shrink-0 ml-auto text-xs font-medium bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-3 py-1 disabled:opacity-50">{{ running ? tr('Running…') : tr('Run') }}</button>
            </div>
          </div>
          <pre :style="{ fontSize: Math.max(fontSize, 13) + 'px' }"
               class="flex-1 min-h-0 m-0 overflow-auto bg-slate-900 text-slate-100 dark:bg-black font-mono px-4 py-3"
               :class="wrap ? 'whitespace-pre-wrap break-words' : 'whitespace-pre'">{{ text || tr('Nothing yet — run your code.') }}</pre>
        </div>
      </div>
    </Teleport>
  </div>
</template>
