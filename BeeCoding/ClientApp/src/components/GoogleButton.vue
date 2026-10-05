<script setup>
import { withBase } from '../lib/base';

const props = defineProps({
  returnTo: { type: String, default: '' },   // app-relative path to land on afterwards
  mode: { type: String, default: 'login' },  // 'login' | 'link'
  label: { type: String, default: 'Continue with Google' },
});

// A full-page round trip: the server redirects to Google and back, then on to the SPA.
function go() {
  const q = new URLSearchParams();
  if (props.mode === 'link') q.set('mode', 'link');
  if (props.returnTo) q.set('r', props.returnTo);
  window.location.href = withBase('/api/auth/google/start') + (q.toString() ? `?${q}` : '');
}
</script>

<template>
  <button type="button" @click="go"
          class="w-full flex items-center justify-center gap-2 border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-800 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-lg py-2 text-sm font-medium text-slate-700 dark:text-slate-200">
    <svg width="18" height="18" viewBox="0 0 48 48" aria-hidden="true">
      <path fill="#EA4335" d="M24 9.5c3.5 0 6.6 1.2 9.1 3.6l6.8-6.8C35.8 2.4 30.3 0 24 0 14.6 0 6.5 5.4 2.6 13.2l7.9 6.1C12.4 13.6 17.7 9.5 24 9.5z"/>
      <path fill="#4285F4" d="M46.5 24.5c0-1.6-.1-3.1-.4-4.5H24v9h12.7c-.6 3-2.3 5.5-4.8 7.2l7.5 5.8c4.4-4.1 7.1-10.1 7.1-17.5z"/>
      <path fill="#FBBC05" d="M10.5 28.7a14.5 14.5 0 0 1 0-9.4l-7.9-6.1a24 24 0 0 0 0 21.6l7.9-6.1z"/>
      <path fill="#34A853" d="M24 48c6.5 0 11.9-2.1 15.9-5.8l-7.5-5.8c-2.1 1.4-4.9 2.3-8.4 2.3-6.3 0-11.6-4.1-13.5-9.8l-7.9 6.1C6.5 42.6 14.6 48 24 48z"/>
    </svg>
    {{ label }}
  </button>
</template>
