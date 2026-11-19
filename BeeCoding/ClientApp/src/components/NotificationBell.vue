<script setup>
import { agoShort as ago } from '../lib/timeAgo';
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useNotifications, notificationText, notificationTarget } from '../stores/notifications';
import { vClickOutside } from '../lib/clickOutside';

const notif = useNotifications();
const router = useRouter();
const open = ref(false);
// Unread ones beyond the loaded page: the badge counts them, the list cannot show them yet.
const hiddenUnread = computed(() => Math.max(0, notif.unread - notif.items.filter((n) => !n.read).length));


// Opening the list already shows everything the toasts do — clear them so they don't cover it.
function toggle() {
  open.value = !open.value;
  if (open.value) notif.toasts = [];
}

async function go(n) {
  open.value = false;
  notif.markRead(n);
  notif.dismissToast(n.id);
  router.push(notificationTarget(n));
}
</script>

<template>
  <div class="relative" v-click-outside="() => (open = false)">
    <button @click="toggle" class="relative px-2 text-xl leading-none transition text-slate-700 dark:text-slate-200"
            :class="notif.unread ? '' : 'opacity-60 hover:opacity-100'"
            :title="tr('Notifications')" :aria-label="tr('Notifications')">
      <Icon name="bell" class="!w-5 !h-5" />
      <span v-if="notif.unread" class="absolute -top-2 -right-1.5 min-w-4 h-4 px-1 rounded-full ring-2 ring-white dark:ring-slate-900 bg-amber-500 text-slate-900 text-[11px] font-bold leading-4 text-center">
        {{ notif.unread > 9 ? '9+' : notif.unread }}
      </span>
    </button>

    <template v-if="open">
      <div class="absolute right-0 z-50 mt-2 w-80 max-w-[calc(100vw-1.5rem)] bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-lg overflow-hidden">
        <div class="flex items-center justify-between px-3 py-2 border-b border-slate-100 dark:border-slate-800">
          <span class="text-sm font-semibold">{{ tr('Notifications') }}</span>
          <span class="flex items-center gap-3">
            <button v-if="notif.unread" @click="notif.markAllRead()" class="text-xs text-amber-700 dark:text-amber-400 hover:underline">{{ tr('Mark all read') }}</button>
            <button v-if="notif.items.some((n) => n.read)" @click="notif.clearRead()" class="text-xs text-slate-500 dark:text-slate-400 hover:underline">{{ tr('Clear read') }}</button>
          </span>
        </div>
        <div class="max-h-96 overflow-y-auto">
          <div v-for="n in notif.items" :key="n.id" @click="go(n)" role="button" tabindex="0" @keydown.enter="go(n)"
               class="w-full text-left px-3 py-2 flex gap-2 border-b border-slate-50 dark:border-slate-800/60 hover:bg-slate-50 dark:hover:bg-slate-800 cursor-pointer"
               :class="{ 'bg-amber-50/60 dark:bg-amber-500/5': !n.read }">
            <span class="mt-1.5 w-2 h-2 rounded-full shrink-0" :class="n.read ? 'bg-transparent' : 'bg-amber-500'"></span>
            <span class="min-w-0 flex-1">
              <span class="block text-sm text-slate-700 dark:text-slate-200 break-words">{{ notificationText(n) }}</span>
              <span class="block text-[11px] text-slate-500 dark:text-slate-400 truncate">{{ n.problemTitle }}<template v-if="n.boardTitle"> · {{ n.boardTitle }}</template> · {{ ago(n.createdAt) }}</span>
            </span>
            <button v-if="n.read" @click.stop="notif.deleteRead(n)" :title="tr('Delete')" :aria-label="tr('Delete notification')"
                    class="shrink-0 self-start text-slate-300 hover:text-rose-500 dark:text-slate-600 dark:hover:text-rose-400 leading-none px-1">✕</button>
          </div>
          <button v-if="notif.items.length < notif.total" @click="notif.loadMore()"
                  class="w-full px-3 py-2 text-xs text-amber-700 dark:text-amber-400 hover:bg-slate-50 dark:hover:bg-slate-800">
            {{ tr('Load more') }}<template v-if="hiddenUnread > 0"> {{ tr('— {n} more unread', { n: hiddenUnread }) }}</template>
          </button>
          <p v-if="!notif.items.length" class="px-3 py-6 text-center text-sm text-slate-500 dark:text-slate-400">
            {{ tr('Nothing yet — reactions and comments on your cards, and updates on problem reports, show up here.') }}
          </p>
        </div>
      </div>
    </template>
  </div>
</template>
