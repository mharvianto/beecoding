<script setup>
import { ref } from 'vue';
import { browserNotifySupported, browserNotifyPermission, browserNotifyWanted, setBrowserNotify, showBrowserNotification } from '../lib/browserNotify';

const supported = browserNotifySupported();
const on = ref(browserNotifyWanted() && browserNotifyPermission() === 'granted');
const denied = ref(browserNotifyPermission() === 'denied');

async function toggle() {
  const r = await setBrowserNotify(!on.value);
  on.value = r === 'on';
  denied.value = browserNotifyPermission() === 'denied';
}

const TEST_DELAY_S = 5;
const countdown = ref(0);
const testMsg = ref('');

// Fires a real desktop notification after a short delay so you can switch to another tab/window and see it arrive.
async function test() {
  if (browserNotifyPermission() !== 'granted') {
    const r = await setBrowserNotify(true);
    on.value = r === 'on';
    denied.value = browserNotifyPermission() === 'denied';
    if (r !== 'on') return;
  }
  testMsg.value = '';
  countdown.value = TEST_DELAY_S;
  const timer = setInterval(() => {
    countdown.value -= 1;
    if (countdown.value > 0) return;
    clearInterval(timer);
    showBrowserNotification({ id: 'test', title: 'BeeCoding', body: 'Test notification: browser notifications work on this device.', force: true });
    testMsg.value = 'Sent. If nothing appeared, check the operating system\'s Do Not Disturb / Focus mode and the browser\'s notification settings.';
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
      <button @click="toggle"
              class="rounded-lg px-4 py-2 text-sm font-medium"
              :class="on ? 'bg-slate-200 dark:bg-slate-700 hover:bg-slate-300 dark:hover:bg-slate-600' : 'bg-amber-500 hover:bg-amber-600 text-white'">
        {{ on ? 'Turn off' : 'Turn on' }}
      </button>
      <button @click="test" :disabled="countdown > 0"
              class="ml-2 rounded-lg px-4 py-2 text-sm font-medium bg-slate-200 dark:bg-slate-700 hover:bg-slate-300 dark:hover:bg-slate-600 disabled:opacity-50">
        {{ countdown > 0 ? `Sending in ${countdown}s… switch to another tab` : 'Send test notification' }}
      </button>
      <p v-if="testMsg" class="text-sm text-slate-500 dark:text-slate-400">{{ testMsg }}</p>
      <p v-if="denied" class="text-sm text-red-600 dark:text-red-400">Notifications are blocked for this site. Allow them in the browser's site settings, then try again.</p>
    </template>
  </section>
</template>
