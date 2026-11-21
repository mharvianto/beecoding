<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { watch } from 'vue';
import { useRouter } from 'vue-router';
import { useBadgeToast } from '../stores/badgeToast';
import { tierOf } from '../lib/badgeTier';

const toast = useBadgeToast();
const router = useRouter();
let timer = null;
watch(() => toast.deadline, (d) => {
  clearTimeout(timer);
  if (d) timer = setTimeout(() => toast.dismiss(), Math.max(0, d - Date.now()));
});
const open = () => { toast.dismiss(); router.push('/account/badges'); };
</script>

<template>
  <Transition enter-active-class="transition duration-200 ease-out" enter-from-class="opacity-0 -translate-y-2"
              leave-active-class="transition duration-150 ease-in" leave-to-class="opacity-0 -translate-y-2">
    <div v-if="toast.visible" role="status" class="fixed z-50 top-16 inset-x-0 flex justify-center pointer-events-none px-4">
      <button type="button" @click="open"
              class="pointer-events-auto flex items-center gap-3 bg-white dark:bg-slate-800 border border-amber-300 dark:border-amber-500/40 shadow-xl rounded-2xl px-4 py-3 text-left max-w-sm">
        <span class="flex flex-col gap-0.5 shrink-0"><Icon v-for="b in toast.badges.slice(0, 3)" :key="b.key" :name="b.icon" class="text-2xl" :class="tierOf(b.tier).icon" /></span>
        <span class="min-w-0">
          <span class="block text-[11px] font-semibold uppercase tracking-wide text-amber-700 dark:text-amber-400">
            {{ toast.badges.length === 1 ? tr('New badge') : tr('{n} new badges', { n: toast.badges.length }) }}
          </span>
          <span v-for="b in toast.badges" :key="b.key" class="block text-sm font-semibold text-slate-900 dark:text-slate-100">{{ tr(b.name) }}
            <span class="font-normal text-xs text-slate-500 dark:text-slate-400">· {{ tr(b.description) }}</span></span>
        </span>
      </button>
    </div>
  </Transition>
</template>
