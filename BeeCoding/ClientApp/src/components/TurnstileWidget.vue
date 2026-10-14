<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useAuth } from '../stores/auth';

// Cloudflare Turnstile human check. Renders only while the server has it switched on (the public site key comes
// with /api/auth/config); otherwise it shows nothing and the token stays empty. `action` must match the action the
// server verifies for that form. v-model:token holds the current token (empty until solved, after it expires or
// fails). A token works once, so call reset() after every submit, whether or not it succeeded.
const props = defineProps({ action: { type: String, required: true }, token: { type: String, default: '' } });
const emit = defineEmits(['update:token']);

const auth = useAuth();
const el = ref(null);
const failed = ref(false);
let widgetId = null;

const SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
let scriptPromise = null;
function loadScript() {
  if (window.turnstile) return Promise.resolve();
  scriptPromise ||= new Promise((resolve, reject) => {
    const s = document.createElement('script');
    s.src = SCRIPT; s.async = true; s.defer = true;
    s.onload = resolve;
    s.onerror = () => { scriptPromise = null; reject(new Error('turnstile script blocked')); };
    document.head.appendChild(s);
  });
  return scriptPromise;
}

onMounted(async () => {
  await auth.loadConfig();
  const sitekey = auth.config.turnstileSiteKey;
  if (!sitekey || !el.value) return;
  try {
    await loadScript();
    if (!el.value) return;   // left the page while the script loaded
    widgetId = window.turnstile.render(el.value, {
      sitekey,
      action: props.action,
      theme: 'auto',
      callback: (t) => { failed.value = false; emit('update:token', t); },
      'expired-callback': () => emit('update:token', ''),
      'error-callback': () => { failed.value = true; emit('update:token', ''); },
    });
  } catch { failed.value = true; }
});

function reset() {
  emit('update:token', '');
  if (widgetId !== null) window.turnstile?.reset(widgetId);
}
onBeforeUnmount(() => { if (widgetId !== null) window.turnstile?.remove(widgetId); });
defineExpose({ reset });
</script>

<template>
  <div v-if="auth.config.turnstileSiteKey">
    <div ref="el"></div>
    <p v-if="failed" class="text-xs text-red-600 dark:text-red-400 mt-1">
      The verification check could not load. Turn off any blocker for this page and reload.
    </p>
  </div>
</template>
