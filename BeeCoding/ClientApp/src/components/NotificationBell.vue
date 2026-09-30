<script setup>
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { useNotifications, notificationText } from '../stores/notifications';

const notif = useNotifications();
const router = useRouter();
const open = ref(false);

function ago(iso) {
  const t = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z');
  const s = Math.max(1, Math.floor((Date.now() - t) / 1000));
  if (s < 60) return 'just now';
  if (s < 3600) return Math.floor(s / 60) + 'm ago';
  if (s < 86400) return Math.floor(s / 3600) + 'h ago';
  return Math.floor(s / 86400) + 'd ago';
}

// Opening the list already shows everything the toasts do — clear them so they don't cover it.
function toggle() {
  open.value = !open.value;
  if (open.value) notif.toasts = [];
}

async function go(n) {
  open.value = false;
  notif.markRead(n);
  notif.dismissToast(n.id);
  router.push({ path: `/boards/${n.boardSlug}`, query: { problem: String(n.problemId), post: String(n.postId) } });
}
</script>

<template>
  <div class="relative">
    <!-- Emoji, like the rest of the app's icons (and the 🔔 chips on wall cards / problem tabs). -->
    <button @click="toggle" class="relative px-1 text-base leading-none transition"
            :class="notif.unread ? '' : 'opacity-60 hover:opacity-100'"
            title="Notifications" aria-label="Notifications">
      🔔
      <span v-if="notif.unread" class="absolute -top-0.5 -right-0.5 min-w-4 h-4 px-1 rounded-full bg-amber-500 text-white text-[10px] font-bold leading-4 text-center">
        {{ notif.unread > 9 ? '9+' : notif.unread }}
      </span>
    </button>

    <template v-if="open">
      <div class="fixed inset-0 z-40" @click="open = false"></div>
      <div class="absolute right-0 z-50 mt-2 w-80 max-w-[calc(100vw-1.5rem)] bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-lg overflow-hidden">
        <div class="flex items-center justify-between px-3 py-2 border-b border-slate-100 dark:border-slate-800">
          <span class="text-sm font-semibold">Notifications</span>
          <button v-if="notif.unread" @click="notif.markAllRead()" class="text-xs text-amber-600 dark:text-amber-400 hover:underline">Mark all read</button>
        </div>
        <div class="max-h-96 overflow-y-auto">
          <button v-for="n in notif.items" :key="n.id" @click="go(n)"
                  class="w-full text-left px-3 py-2 flex gap-2 border-b border-slate-50 dark:border-slate-800/60 hover:bg-slate-50 dark:hover:bg-slate-800"
                  :class="{ 'bg-amber-50/60 dark:bg-amber-500/5': !n.read }">
            <span class="mt-1.5 w-2 h-2 rounded-full shrink-0" :class="n.read ? 'bg-transparent' : 'bg-amber-500'"></span>
            <span class="min-w-0">
              <span class="block text-sm text-slate-700 dark:text-slate-200 break-words">{{ notificationText(n) }}</span>
              <span class="block text-[11px] text-slate-400 dark:text-slate-500 truncate">{{ n.problemTitle }} · {{ n.boardTitle }} · {{ ago(n.createdAt) }}</span>
            </span>
          </button>
          <p v-if="!notif.items.length" class="px-3 py-6 text-center text-sm text-slate-400 dark:text-slate-500">
            Nothing yet — reactions and comments on your cards show up here.
          </p>
        </div>
      </div>
    </template>
  </div>
</template>
