<script setup>
import { tr } from '../lib/i18n';
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue';
import { ioFolded, setIoFolded } from '../lib/ioFold';
import Icon from './Icon.vue';

const props = defineProps({
  direction: { type: String, default: 'horizontal' },  // 'horizontal' = side by side, 'vertical' = stacked
  initial: { type: Number, default: 50 },               // starting % for pane A
  initialStacked: { type: Number, default: 0 },         // starting % for pane A when a horizontal split is stacked (0 = use `initial`)
  min: { type: Number, default: 120 },                  // px min for either pane
  storageKey: { type: String, default: '' },
  collapseBelow: { type: Number, default: 1024 },       // a horizontal split lays out stacked under this width (still draggable)
  foldable: { type: Boolean, default: false },          // vertical only: a slim bar under pane A folds pane B away (per device); A then takes everything
  hideB: { type: Boolean, default: false },             // collapse pane B entirely — pane A fills, no divider
});

const fold = computed(() => props.foldable && ioFolded.value);
const el = ref(null);
const wide = ref(typeof window === 'undefined' || window.innerWidth >= props.collapseBelow);
function updateWide() { wide.value = window.innerWidth >= props.collapseBelow; }

// side-by-side only for a wide horizontal split; otherwise a top/bottom split — still draggable.
const isRow = computed(() => props.direction === 'horizontal' && wide.value);
const effMin = computed(() => (wide.value ? props.min : Math.min(props.min, 104)));
// a horizontal split stores width-% and, when stacked on a phone, height-% — keep those
// under separate keys. A pure vertical split keeps its original key.
const storeKey = computed(() =>
  !props.storageKey ? ''
  : (props.direction === 'horizontal' && !isRow.value) ? props.storageKey + ':v'
  : props.storageKey);

const pct = ref(props.initial);
function loadPct() {
  pct.value = (!isRow.value && props.initialStacked) ? props.initialStacked : props.initial;
  try {
    const v = parseFloat(localStorage.getItem(storeKey.value));
    if (!Number.isNaN(v) && v > 4 && v < 96) pct.value = v;
  } catch { /* ignore */ }
}
loadPct();
watch(isRow, loadPct);

let dragging = false, moved = true, downY = 0;
function onDown(e) {
  dragging = true; moved = !props.foldable; downY = e.clientY;   // a foldable handle is also a button: a press that never moves is a click
  document.body.style.userSelect = 'none';
  document.body.style.cursor = isRow.value ? 'col-resize' : 'row-resize';
}
function onMove(e) {
  if (!dragging || !el.value) return;
  if (!moved) {
    if (Math.abs(e.clientY - downY) < 4) return;
    moved = true;
    if (ioFolded.value) setIoFolded(false);   // dragging a folded panel open
  }
  const r = el.value.getBoundingClientRect();
  const total = isRow.value ? r.width : r.height;
  if (total <= 0) return;
  const pos = isRow.value ? e.clientX - r.left : e.clientY - r.top;
  const minPct = (effMin.value / total) * 100;
  pct.value = Math.max(minPct, Math.min(100 - minPct, (pos / total) * 100));
}
function onUp() {
  if (!dragging) return;
  dragging = false;
  document.body.style.userSelect = '';
  document.body.style.cursor = '';
  if (!moved) { setIoFolded(!ioFolded.value); return; }
  try { if (storeKey.value) localStorage.setItem(storeKey.value, String(Math.round(pct.value * 10) / 10)); } catch { /* ignore */ }
}

// keyboard on the foldable handle: Enter/Space fold, Up/Down move the divider by 3%
function onKey(e) {
  if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setIoFolded(!ioFolded.value); return; }
  if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
  e.preventDefault();
  if (ioFolded.value) setIoFolded(false);
  pct.value = Math.max(10, Math.min(90, pct.value + (e.key === 'ArrowUp' ? -3 : 3)));
  try { if (storeKey.value) localStorage.setItem(storeKey.value, String(Math.round(pct.value * 10) / 10)); } catch { /* ignore */ }
}

onMounted(() => {
  updateWide();
  window.addEventListener('resize', updateWide);
  window.addEventListener('pointermove', onMove);
  window.addEventListener('pointerup', onUp);
  window.addEventListener('pointercancel', onUp);
});
onBeforeUnmount(() => {
  window.removeEventListener('resize', updateWide);
  window.removeEventListener('pointermove', onMove);
  window.removeEventListener('pointerup', onUp);
  window.removeEventListener('pointercancel', onUp);
});
</script>

<template>
  <div ref="el" class="min-h-0 min-w-0 h-full w-full flex" :class="isRow ? 'flex-row' : 'flex-col'">
    <div class="min-h-0 min-w-0 overflow-hidden"
         :class="hideB || fold ? 'flex-1' : ''"
         :style="hideB || fold ? null : (isRow ? { width: pct + '%' } : { height: pct + '%' })">
      <slot name="a" />
    </div>

    <!-- foldable: ONE bar is both the drag handle and the fold button (a press without movement folds) -->
    <div v-if="foldable && !hideB" role="separator" aria-orientation="horizontal" tabindex="0" @pointerdown.prevent="onDown" @keydown="onKey"
         :aria-expanded="!ioFolded" :aria-valuenow="Math.round(ioFolded ? 100 : pct)" aria-valuemin="0" aria-valuemax="100" :aria-label="tr('Run panel')"
         :title="tr('Drag to resize, click to fold')"
         class="group relative shrink-0 z-10 h-8 md:h-6 flex items-center gap-1 px-3 text-xs touch-none select-none cursor-row-resize
                text-slate-500 dark:text-slate-400 bg-slate-50 dark:bg-slate-900 border-y border-slate-200 dark:border-slate-800
                hover:border-amber-400 dark:hover:border-amber-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-amber-500">
      <Icon name="chevron" class="transition-transform" :class="ioFolded ? '-rotate-90' : ''" /> {{ tr('Run panel') }}
      <span class="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 h-0.5 w-10 rounded-full bg-slate-400/80 dark:bg-slate-500/80 group-hover:bg-amber-400"></span>
    </div>

    <div v-if="!hideB && !foldable" @pointerdown.prevent="onDown"
         class="group shrink-0 z-10 flex items-center justify-center touch-none select-none
                bg-slate-200 dark:bg-slate-800 hover:bg-amber-400 dark:hover:bg-amber-500 active:bg-amber-400 transition-colors"
         :class="isRow ? 'w-1.5 cursor-col-resize' : 'h-3 md:h-1.5 cursor-row-resize'"
         :title="tr('Drag to resize')">
      <span class="rounded-full bg-slate-400/80 dark:bg-slate-500/80 group-hover:bg-white/90 group-active:bg-white/90"
            :class="isRow ? 'w-0.5 h-8' : 'h-0.5 w-10'"></span>
    </div>

    <div v-if="!hideB && !fold" class="flex-1 min-h-0 min-w-0 overflow-hidden">
      <slot name="b" />
    </div>
  </div>
</template>
