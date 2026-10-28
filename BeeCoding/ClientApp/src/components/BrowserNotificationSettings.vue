<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import {
  browserNotifySupported, browserNotifyPermission, browserNotifyWanted, setBrowserNotify, showBrowserNotification,
  pushSupported, pushDeviceOn, pushConfig, subscribePush, unsubscribePush,
} from '../lib/browserNotify';

const supported = browserNotifySupported();
const on = ref(browserNotifyWanted() && browserNotifyPermission() === 'granted');
const denied = ref(browserNotifyPermission() === 'denied');

// Web Push: delivered by the server through the browser's push service, so it works with the tab closed.
const push = ref(null);                       // { enabled, publicKey } once loaded
const pushOn = ref(pushDeviceOn() && browserNotifyPermission() === 'granted');
const pushBusy = ref(false);
const canPush = () => pushSupported() && push.value?.enabled;
onMounted(async () => { push.value = await pushConfig(api); });

async function toggle() {
  const r = await setBrowserNotify(!on.value);
  on.value = r === 'on';
  denied.value = browserNotifyPermission() === 'denied';
}

async function togglePush() {
  pushBusy.value = true;
  try {
    if (pushOn.value) { await unsubscribePush(api); pushOn.value = false; }
    else pushOn.value = (await subscribePush(api, push.value.publicKey)) === 'on';
    denied.value = browserNotifyPermission() === 'denied';
  } finally { pushBusy.value = false; }
}

const TEST_DELAY_S = 5;
const countdown = ref(0);
const testMsg = ref('');
const HELP = 'If nothing appeared, check the operating system\'s Do Not Disturb / Focus mode and the browser\'s notification settings.';

// Fires a real notification after a short delay so you can switch to another tab/window (or close this one) and see it arrive.
async function test() {
  if (browserNotifyPermission() !== 'granted') {
    const r = await setBrowserNotify(true);
    on.value = r === 'on';
    denied.value = browserNotifyPermission() === 'denied';
    if (r !== 'on') return;
  }
  testMsg.value = '';
  countdown.value = TEST_DELAY_S;
  const timer = setInterval(async () => {
    countdown.value -= 1;
    if (countdown.value > 0) return;
    clearInterval(timer);
    if (pushOn.value) {
      try {
        const { sent } = await api.post('/api/me/push/test');
        testMsg.value = sent > 0 ? `Push sent to ${sent} device${sent === 1 ? '' : 's'}. ${HELP}` : 'The server has no working push subscription for you. Turn push off and on again.';
      } catch (e) { testMsg.value = `Could not send the push: ${e.message}`; }
    } else {
      showBrowserNotification({ id: 'test', title: 'BeeCoding', body: 'Test notification: browser notifications work on this device.', force: true });
      testMsg.value = `Sent. ${HELP}`;
    }
  }, 1000);
}
</script>

<template>
  <section class="space-y-3">
    <h2 class="font-semibold text-sm">Browser notifications</h2>
    <p v-if="!supported" class="text-sm text-slate-500 dark:text-slate-400">This browser does not support desktop notifications.</p>
    <template v-else>
      <p class="text-sm text-slate-500 dark:text-slate-400">
        Show a desktop notification for new bell items while this tab is in the background. Applies to this browser only.
      </p>
      <button @click="toggle" :disabled="pushOn"
              class="rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
              :class="on ? 'bg-slate-200 dark:bg-slate-700 hover:bg-slate-300 dark:hover:bg-slate-600' : 'bg-amber-500 hover:bg-amber-600 text-white'">
        {{ on ? 'Turn off' : 'Turn on' }}
      </button>

      <div v-if="canPush()" class="pt-2 space-y-2">
        <h3 class="font-medium text-sm">Push notifications</h3>
        <p class="text-sm text-slate-500 dark:text-slate-400">
          Also receive them when BeeCoding is closed. This browser registers with its push service; turn it off here to stop.
          On iPhone/iPad, add BeeCoding to the Home Screen first.
        </p>
        <button @click="togglePush" :disabled="pushBusy"
                class="rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
                :class="pushOn ? 'bg-slate-200 dark:bg-slate-700 hover:bg-slate-300 dark:hover:bg-slate-600' : 'bg-amber-500 hover:bg-amber-600 text-white'">
          {{ pushBusy ? '…' : pushOn ? 'Turn off push' : 'Turn on push' }}
        </button>
      </div>

      <div>
        <button @click="test" :disabled="countdown > 0"
                class="rounded-lg px-4 py-2 text-sm font-medium bg-slate-200 dark:bg-slate-700 hover:bg-slate-300 dark:hover:bg-slate-600 disabled:opacity-50">
          {{ countdown > 0 ? `Sending in ${countdown}s… switch to another tab` : 'Send test notification' }}
        </button>
      </div>
      <p v-if="testMsg" class="text-sm text-slate-500 dark:text-slate-400">{{ testMsg }}</p>
      <p v-if="denied" class="text-sm text-red-600 dark:text-red-400">Notifications are blocked for this site. Allow them in the browser's site settings, then try again.</p>
    </template>
  </section>
</template>
