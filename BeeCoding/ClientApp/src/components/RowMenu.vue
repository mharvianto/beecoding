<script setup>
import { ref, nextTick, onBeforeUnmount } from 'vue';

// A "⋮" button that opens a small action menu. The panel is teleported to <body> and positioned with
// fixed coordinates, so it isn't clipped by an overflow-x-auto table; it flips upward near the bottom
// of the screen. Closes on outside click, Escape, scroll, resize, or after picking an item.
// Items are plain <button class="row-menu-item"> (see style.css) in the default slot.
defineProps({ label: { type: String, default: 'Actions' } });

const open = ref(false);
const btn = ref(null);
const panel = ref(null);
const pos = ref({ top: 0, left: 0 });

function place() {
  if (!btn.value || !panel.value) return;
  const r = btn.value.getBoundingClientRect();
  const { offsetHeight: h, offsetWidth: w } = panel.value;
  let top = r.bottom + 4;
  if (top + h > window.innerHeight - 8) top = Math.max(8, r.top - h - 4);
  pos.value = { top, left: Math.min(Math.max(8, r.right - w), window.innerWidth - w - 8) };
}

// A page scroll would leave the fixed panel floating away from its button: close instead.
let openedAt = 0;
function onScroll(e) {
  if (performance.now() - openedAt < 200) return;   // a scroll that was already under way when it opened
  if (e.target instanceof Node && panel.value?.contains(e.target)) return;
  close();
}
function onDocDown(e) {
  if (!panel.value?.contains(e.target) && !btn.value?.contains(e.target)) close();
}
function onKey(e) {
  if (e.key === 'Escape') { close(); btn.value?.focus(); return; }
  if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
  const items = [...panel.value.querySelectorAll('button')];
  if (!items.length) return;
  e.preventDefault();
  const i = items.indexOf(document.activeElement);
  items[(i + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length].focus();
}

async function show() {
  open.value = true;
  openedAt = performance.now();
  await nextTick();
  place();
  panel.value?.querySelector('button')?.focus({ preventScroll: true });
  document.addEventListener('mousedown', onDocDown);
  document.addEventListener('keydown', onKey);
  window.addEventListener('resize', close);
  window.addEventListener('scroll', onScroll, true);
}
function close() {
  open.value = false;
  document.removeEventListener('mousedown', onDocDown);
  document.removeEventListener('keydown', onKey);
  window.removeEventListener('resize', close);
  window.removeEventListener('scroll', onScroll, true);
}
const toggle = () => (open.value ? close() : show());
onBeforeUnmount(close);
</script>

<template>
  <button ref="btn" type="button" @click="toggle" :aria-label="label" :title="label" aria-haspopup="menu" :aria-expanded="open"
          class="inline-flex items-center justify-center w-8 h-8 rounded-md text-lg leading-none text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-100 dark:hover:bg-slate-800"
          :class="{ 'bg-slate-100 dark:bg-slate-800': open }">⋮</button>
  <Teleport to="body">
    <div v-if="open" ref="panel" role="menu" @click="close" :style="{ top: pos.top + 'px', left: pos.left + 'px' }"
         class="fixed z-50 min-w-48 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-lg py-1">
      <slot />
    </div>
  </Teleport>
</template>
