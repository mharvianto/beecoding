<script setup>
import { ref, computed, watch, onUnmounted } from 'vue';
import { useUndoToast } from '../stores/undoToast';

const toast = useUndoToast();
const now = ref(Date.now());
let ticker = null;

function startTicking() {
  clearInterval(ticker);
  ticker = setInterval(() => {
    now.value = Date.now();
    if (now.value >= toast.deadline) toast.dismiss();
  }, 250);
}
watch(() => toast.visible, (v) => { if (v) startTicking(); else clearInterval(ticker); }, { immediate: true });
onUnmounted(() => clearInterval(ticker));

const secondsLeft = computed(() => Math.max(0, Math.ceil((toast.deadline - now.value) / 1000)));
</script>

<template>
  <Transition enter-active-class="transition duration-150 ease-out" enter-from-class="opacity-0 translate-y-2"
              leave-active-class="transition duration-150 ease-in" leave-to-class="opacity-0 translate-y-2">
    <div v-if="toast.visible"
         class="fixed z-50 bottom-4 left-1/2 -translate-x-1/2 sm:left-auto sm:right-4 sm:translate-x-0
                bg-slate-800 dark:bg-slate-700 text-white rounded-xl shadow-lg px-4 py-2.5
                flex items-center gap-3 text-sm max-w-[calc(100vw-2rem)]">
      <span class="truncate">{{ toast.message }}</span>
      <button v-if="toast.onUndo" @click="toast.undo()"
              class="shrink-0 font-semibold text-amber-300 hover:text-amber-200 underline underline-offset-2">
        Undo ({{ secondsLeft }}s)
      </button>
      <button @click="toast.dismiss()" class="shrink-0 text-slate-400 hover:text-slate-200" title="Dismiss">✕</button>
    </div>
  </Transition>
</template>
