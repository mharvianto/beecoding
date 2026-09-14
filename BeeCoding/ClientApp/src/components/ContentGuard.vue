<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';

/**
 * Deters casual copying / screenshots of the wrapped content:
 *  - blocks text selection, copy/cut, context menu, drag
 *  - blurs the content while the tab is hidden or the window loses focus
 *    (covers app-switching on mobile and most screenshot / screen-record tools)
 *  - overlays a faint diagonal watermark (the viewer's identity) so any
 *    capture that does get through is attributable
 *  - disables printing of the page
 *
 * It cannot stop someone pointing a second camera at the screen — nothing web
 * or native can. The watermark is the real safeguard there.
 */
const props = defineProps({
  watermark: { type: String, default: '' },
  active: { type: Boolean, default: true },
});

const hidden = ref(false);

function onVisibility() { if (props.active) hidden.value = document.hidden; }
function onBlur() { if (props.active) hidden.value = true; }
function onFocus() { if (props.active) hidden.value = document.hidden; }
function onKey(e) {
  if (!props.active) return;
  if (e.key === 'PrintScreen') {
    try { navigator.clipboard?.writeText(''); } catch { /* ignore */ }
    hidden.value = true;
    setTimeout(() => { hidden.value = document.hidden; }, 800);
    return;
  }
  // The guarded content (an image/canvas) is never itself focusable, so a
  // keydown while looking at it still targets document.body — there's no
  // reliable "focus is inside the guard" signal to scope to. Instead carve
  // out the one legitimate place on these pages that genuinely needs
  // copy/paste/select-all: the code editor. Everything else keeps the
  // blanket block.
  if (e.target?.closest?.('.monaco-editor')) return;
  const k = (e.key || '').toLowerCase();
  if ((e.ctrlKey || e.metaKey) && ['c', 'x', 's', 'p', 'a'].includes(k)) e.preventDefault();
}

onMounted(() => {
  if (!props.active) return;
  document.documentElement.classList.add('protected');
  document.addEventListener('visibilitychange', onVisibility);
  window.addEventListener('blur', onBlur);
  window.addEventListener('focus', onFocus);
  window.addEventListener('keydown', onKey, true);
});
onBeforeUnmount(() => {
  document.documentElement.classList.remove('protected');
  document.removeEventListener('visibilitychange', onVisibility);
  window.removeEventListener('blur', onBlur);
  window.removeEventListener('focus', onFocus);
  window.removeEventListener('keydown', onKey, true);
});

const watermarkStyle = computed(() => {
  if (!props.active || !props.watermark) return {};
  const t = String(props.watermark).replace(/[<>&]/g, '');
  const svg =
    `<svg xmlns='http://www.w3.org/2000/svg' width='320' height='170'>` +
    `<text x='8' y='95' transform='rotate(-24 8 95)' fill='rgba(130,130,130,0.16)' ` +
    `font-family='sans-serif' font-size='14'>${t}</text></svg>`;
  return { backgroundImage: `url("data:image/svg+xml;utf8,${encodeURIComponent(svg)}")` };
});
</script>

<template>
  <div class="relative" :class="{ 'select-none': active }"
       @contextmenu="active && $event.preventDefault()"
       @copy="active && $event.preventDefault()"
       @cut="active && $event.preventDefault()"
       @dragstart="active && $event.preventDefault()">
    <div :class="{ 'blur-md': active && hidden }" class="transition-[filter] duration-150">
      <slot />
    </div>

    <div v-if="active && watermark"
         class="pointer-events-none absolute inset-0 bg-repeat" :style="watermarkStyle"></div>

    <div v-if="active && hidden"
         class="absolute inset-0 grid place-items-center rounded-lg bg-slate-100/85 dark:bg-slate-900/85 text-sm text-slate-500 dark:text-slate-400">
      👁️ Return to this tab to view the problem
    </div>
  </div>
</template>
