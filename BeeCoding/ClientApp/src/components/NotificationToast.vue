<script setup>
import { useRouter } from 'vue-router';
import { useNotifications, notificationText } from '../stores/notifications';

const notif = useNotifications();
const router = useRouter();

function go(n) {
  notif.dismissToast(n.id);
  notif.markRead(n);
  router.push({ path: `/boards/${n.boardSlug}`, query: { problem: String(n.problemId), post: String(n.postId) } });
}
</script>

<template>
  <div class="fixed z-50 top-16 right-3 left-3 sm:left-auto sm:w-80 flex flex-col gap-2 pointer-events-none">
    <TransitionGroup enter-active-class="transition duration-150 ease-out" enter-from-class="opacity-0 -translate-y-2"
                     leave-active-class="transition duration-150 ease-in" leave-to-class="opacity-0">
      <button v-for="n in notif.toasts" :key="n.id" @click="go(n)"
              class="pointer-events-auto text-left bg-slate-800 dark:bg-slate-700 text-white rounded-xl shadow-lg px-3 py-2 flex items-start gap-2 text-sm">
        <span class="shrink-0">{{ n.kind === 'comment' ? '💬' : (n.emoji || '🔔') }}</span>
        <span class="min-w-0">
          <span class="block break-words">{{ notificationText(n) }}</span>
          <span class="block text-[11px] text-slate-300 truncate">{{ n.problemTitle }} · {{ n.boardTitle }}</span>
        </span>
        <span @click.stop="notif.dismissToast(n.id)" class="shrink-0 text-slate-400 hover:text-slate-200" title="Dismiss">✕</span>
      </button>
    </TransitionGroup>
  </div>
</template>
