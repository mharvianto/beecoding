<script setup>
import { ref } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import {
  browserNotifySupported, browserNotifyPermission, browserNotifyWanted, setBrowserNotify,
  pushSupported, pushDeviceOn, pushConfig, subscribePush,
} from '../lib/browserNotify';

// Soft offer to turn on notifications (the bell's items as a desktop / push notification). Shown once per device to a
// signed-in user whose browser supports it and who has not already turned it on or blocked it. "Not now" hides it for
// two weeks, "Don't ask again" for good. Kept in localStorage on purpose: the browser's permission is per device, so a
// choice made on the laptop must not hide the offer on the phone.
const auth = useAuth();
const key = () => `beecoding.notifyOffer.${auth.user?.id}`;

function hiddenNow() {
  try { return Number(localStorage.getItem(key()) || 0) > Date.now(); } catch { return false; }
}
function eligible() {
  if (!browserNotifySupported()) return false;
  const perm = browserNotifyPermission();
  if (perm === 'denied') return false;                                   // blocked in the browser: nothing we can ask for
  if (perm === 'granted' && (pushDeviceOn() || browserNotifyWanted())) return false;   // already on here
  return !hiddenNow();
}

const shown = ref(eligible());
const busy = ref(false);
const message = ref('');

function hide(forever) {
  try { localStorage.setItem(key(), String(forever ? Number.MAX_SAFE_INTEGER : Date.now() + 14 * 86400_000)); } catch { /* it shows again next load */ }
  shown.value = false;
}

async function turnOn() {
  busy.value = true; message.value = '';
  try {
    const cfg = await pushConfig(api);
    // Web Push works with the tab closed (needs the server's VAPID keys); otherwise a desktop notification while the tab is in the background.
    const r = pushSupported() && cfg?.enabled ? await subscribePush(api, cfg.publicKey) : await setBrowserNotify(true);
    if (r === 'on') {
      message.value = 'Notifications are on for this browser. You can change that any time under Account.';
      setTimeout(() => { shown.value = false; }, 4000);
    } else {
      message.value = 'Your browser blocked notifications. Allow them in the site settings if you change your mind.';
      hide(false);
      setTimeout(() => { shown.value = false; }, 6000);
      shown.value = true;
    }
  } finally { busy.value = false; }
}
</script>

<template>
  <div v-if="shown" class="shrink-0 bg-amber-50 dark:bg-amber-500/10 border-b border-amber-200 dark:border-amber-500/20 text-sm text-amber-900 dark:text-amber-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span v-if="message">{{ message }}</span>
      <template v-else>
        <span>
          🔔 Turn on notifications
          <span class="hidden sm:inline">— hear about comments, reactions and reviews even when this tab is in the background.</span>
        </span>
        <button @click="turnOn" :disabled="busy" class="font-semibold underline hover:no-underline disabled:opacity-50">{{ busy ? '…' : 'Turn on' }}</button>
        <span class="ml-auto flex items-center gap-3 text-xs text-amber-700 dark:text-amber-300">
          <button @click="hide(false)" class="hover:underline">Not now</button>
          <button @click="hide(true)" class="hover:underline">Don't ask again</button>
        </span>
      </template>
    </div>
  </div>
</template>
