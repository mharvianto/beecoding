<script setup>
import { watch } from 'vue';
import { useCelebrationToast } from '../stores/celebrationToast';

const toast = useCelebrationToast();
let timer = null;
watch(() => toast.deadline, (d) => {
  clearTimeout(timer);
  if (!d) return;
  timer = setTimeout(() => toast.dismiss(), Math.max(0, d - Date.now()));
});
</script>

<template>
  <Transition enter-active-class="transition duration-200 ease-out" enter-from-class="opacity-0 scale-90"
              leave-active-class="transition duration-150 ease-in" leave-to-class="opacity-0 scale-90">
    <div v-if="toast.visible" class="fixed z-50 inset-0 flex items-center justify-center pointer-events-none px-4">
      <div class="pointer-events-auto bg-white dark:bg-slate-800 border border-amber-200 dark:border-amber-500/30
                  shadow-xl rounded-2xl px-6 py-4 text-center max-w-xs" @click="toast.dismiss()">
        <div class="text-3xl mb-1">🎉</div>
        <div class="font-bold text-lg text-slate-900 dark:text-slate-100">
          {{ toast.xpGained > 0 ? `+${toast.xpGained} XP` : 'Accepted!' }}
        </div>
        <div class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          Lv {{ toast.level }} · {{ toast.xp }} XP
        </div>
        <div class="text-xs text-amber-600 dark:text-amber-400 mt-1.5 font-medium">
          🔥 {{ toast.solvedToday }} solved today
        </div>
      </div>
    </div>
  </Transition>
</template>
