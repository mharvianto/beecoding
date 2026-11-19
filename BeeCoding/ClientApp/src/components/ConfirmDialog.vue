<script setup>
import { tr } from '../lib/i18n';
import { useConfirmDialog } from '../stores/confirmDialog';

const dialog = useConfirmDialog();
</script>

<template>
  <Transition enter-active-class="transition duration-150 ease-out" enter-from-class="opacity-0"
              leave-active-class="transition duration-100 ease-in" leave-to-class="opacity-0">
    <div v-if="dialog.visible" role="dialog" aria-modal="true" :aria-label="tr('Confirm')" class="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50"
         @keydown.esc="dialog.cancel()" @click.self="dialog.cancel()">
      <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-sm p-5 shadow-lg">
        <p class="text-sm text-slate-700 dark:text-slate-200 whitespace-pre-wrap">{{ tr(dialog.message) }}</p>
        <div class="flex justify-end gap-2 mt-5">
          <button @click="dialog.cancel()"
                  class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">
            {{ tr(dialog.cancelLabel) }}
          </button>
          <button @click="dialog.confirm()" autofocus
                  class="px-3 py-1.5 rounded-lg text-sm font-medium text-white"
                  :class="dialog.danger ? 'bg-rose-600 hover:bg-rose-700' : 'bg-amber-500 hover:bg-amber-600'">
            {{ tr(dialog.confirmLabel) }}
          </button>
        </div>
      </div>
    </div>
  </Transition>
</template>
