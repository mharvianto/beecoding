<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { ref, onMounted } from 'vue';

// Shown once, ever, per browser — only when the device actually has reduce-motion on,
// so a student doesn't wonder why solving never confetti's. Not a nag to change an
// accessibility setting, just an explanation; the celebration toast itself always still
// shows (see lib/confetti.js) — only the particle animation is skipped for these users.
const SEEN_KEY = 'beecoding.reducedMotionNoticeSeen';
const visible = ref(false);
const expanded = ref(false);

onMounted(() => {
  try {
    if (localStorage.getItem(SEEN_KEY)) return;
    if (!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
    visible.value = true;
    localStorage.setItem(SEEN_KEY, '1');
  } catch { /* ignore */ }
});
function dismiss() { visible.value = false; }
</script>

<template>
  <Transition enter-active-class="transition duration-150 ease-out" enter-from-class="opacity-0 translate-y-2"
              leave-active-class="transition duration-150 ease-in" leave-to-class="opacity-0 translate-y-2">
    <div v-if="visible"
         class="fixed z-50 bottom-4 left-1/2 -translate-x-1/2 sm:left-auto sm:right-4 sm:translate-x-0
                bg-slate-800 dark:bg-slate-700 text-white rounded-xl shadow-lg px-4 py-2.5
                text-sm max-w-[calc(100vw-2rem)] sm:max-w-sm">
      <div class="flex items-center gap-3">
        <button @click="expanded = !expanded" class="text-left flex-1 hover:underline">
          <Icon name="sparkles" /> {{ tr('Animations are reduced, following your device setting') }}
        </button>
        <button @click="dismiss" class="shrink-0 text-slate-400 hover:text-slate-200" :title="tr('Dismiss')" :aria-label="tr('Dismiss')">✕</button>
      </div>
      <div v-if="expanded" class="mt-2 pt-2 border-t border-slate-600 dark:border-slate-500 text-xs text-slate-300 dark:text-slate-200 space-y-1">
        <p><strong>Windows 11:</strong> {{ tr('Settings → Accessibility → Visual effects → turn on "Animation effects"') }}</p>
        <p><strong>Windows 10:</strong> {{ tr('Settings → Ease of Access → Display → turn on "Show animations in Windows"') }}</p>
        <p><strong>macOS:</strong> {{ tr('System Settings → Accessibility → Display → turn off "Reduce motion"') }}</p>
        <p class="text-slate-500 dark:text-slate-300">{{ tr('This is an accessibility setting on your device — just for information, nothing you must change.') }}</p>
      </div>
    </div>
  </Transition>
</template>
