<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useAuth } from '../stores/auth';
import { isDark } from '../lib/theme';

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

let sitekey = '';
let shownDark = false;
let themeObserver = null;

// The widget follows the app's own light/dark choice (not just the operating system's), at the form's full width
// ('flexible' fills its container, which is as wide as the inputs and the button).
function draw() {
  shownDark = isDark();
  widgetId = window.turnstile.render(el.value, {
    sitekey,
    action: props.action,
    theme: shownDark ? 'dark' : 'light',
    size: 'flexible',
    callback: (t) => { failed.value = false; emit('update:token', t); },
    'expired-callback': () => emit('update:token', ''),
    'error-callback': () => { failed.value = true; emit('update:token', ''); },
  });
}

onMounted(async () => {
  await auth.loadConfig();
  sitekey = auth.config.turnstileSiteKey || '';
  if (!sitekey || !el.value) return;
  try {
    await loadScript();
    if (!el.value) return;   // left the page while the script loaded
    draw();
    // A theme switch (or the system flipping) needs a fresh widget, since its theme is fixed when drawn. That
    // clears the token, so the person is verified again; it only happens when the theme actually changes.
    themeObserver = new MutationObserver(() => {
      if (widgetId === null || isDark() === shownDark) return;
      window.turnstile.remove(widgetId);
      emit('update:token', '');
      draw();
    });
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  } catch { failed.value = true; }
});

function reset() {
  emit('update:token', '');
  if (widgetId !== null) window.turnstile?.reset(widgetId);
}
onBeforeUnmount(() => {
  themeObserver?.disconnect();
  if (widgetId !== null) window.turnstile?.remove(widgetId);
});
defineExpose({ reset });
</script>

<template>
  <div v-if="auth.config.turnstileSiteKey">
    <div ref="el" class="w-full"></div>
    <p v-if="failed" class="text-xs text-red-600 dark:text-red-400 mt-1">
      The verification check could not load. Turn off any blocker for this page and reload.
    </p>
  </div>
</template>
