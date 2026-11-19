<script setup>
import { tr } from '../lib/i18n';
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue';

const props = defineProps({
  direction: { type: String, default: 'horizontal' },  // 'horizontal' = side by side, 'vertical' = stacked
  initial: { type: Number, default: 50 },               // starting % for pane A
  initialStacked: { type: Number, default: 0 },         // starting % for pane A when a horizontal split is stacked (0 = use `initial`)
  min: { type: Number, default: 120 },                  // px min for either pane
  storageKey: { type: String, default: '' },
  collapseBelow: { type: Number, default: 1024 },       // a horizontal split lays out stacked under this width (still draggable)
  hideB: { type: Boolean, default: false },             // collapse pane B entirely — pane A fills, no divider
});

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

let dragging = false;
function onDown() {
  dragging = true;
  document.body.style.userSelect = 'none';
  document.body.style.cursor = isRow.value ? 'col-resize' : 'row-resize';
}
function onMove(e) {
  if (!dragging || !el.value) return;
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
         :class="hideB ? 'flex-1' : ''"
         :style="hideB ? null : (isRow ? { width: pct + '%' } : { height: pct + '%' })">
      <slot name="a" />
    </div>

    <div v-if="!hideB" @pointerdown.prevent="onDown"
         class="group shrink-0 z-10 flex items-center justify-center touch-none select-none
                bg-slate-200 dark:bg-slate-800 hover:bg-amber-400 dark:hover:bg-amber-500 active:bg-amber-400 transition-colors"
         :class="isRow ? 'w-1.5 cursor-col-resize' : 'h-3 md:h-1.5 cursor-row-resize'"
         :title="tr('Drag to resize')">
      <span class="rounded-full bg-slate-400/80 dark:bg-slate-500/80 group-hover:bg-white/90 group-active:bg-white/90"
            :class="isRow ? 'w-0.5 h-8' : 'h-0.5 w-10'"></span>
    </div>

    <div v-if="!hideB" class="flex-1 min-h-0 min-w-0 overflow-hidden">
      <slot name="b" />
    </div>
  </div>
</template>
