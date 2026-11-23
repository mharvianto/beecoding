<script setup>
import { tr } from '../lib/i18n';
import { clientFlags } from '../lib/automation';
import { ref, watch, onBeforeUnmount } from 'vue';
import { theme as appTheme } from '../lib/theme';
import { withBase } from '../lib/base';

const props = defineProps({
  problemSlug: { type: String, default: null },   // board problem
  bankSlug: { type: String, default: null },      // practice (bank) problem
});
const endpoint = () =>
  withBase(props.bankSlug != null
    ? `/api/practice/${encodeURIComponent(props.bankSlug)}/statement`
    : `/api/problems/${encodeURIComponent(props.problemSlug)}/statement`);

const src = ref('');
const err = ref('');
let objectUrl = '';

function fromB64(s) {
  const bin = atob(s);
  const a = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i);
  return a;
}
function revoke() {
  if (objectUrl) { URL.revokeObjectURL(objectUrl); objectUrl = ''; }
}

async function load() {
  err.value = '';
  try {
    const dark = document.documentElement.classList.contains('dark');
    const res = await fetch(`${endpoint()}?theme=${dark ? 'dark' : 'light'}`,
      { credentials: 'include', headers: clientFlags() ? { 'X-Client-Flags': clientFlags() } : {} });
    if (res.status === 403 && clientFlags()) { err.value = 'Automated browsers cannot open protected problems. Use a regular browser window.'; return; }
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const j = await res.json();
    const key = await crypto.subtle.importKey('raw', fromB64(j.key), 'AES-GCM', false, ['decrypt']);
    const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: fromB64(j.iv) }, key, fromB64(j.data));
    revoke();
    objectUrl = URL.createObjectURL(new Blob([plain], { type: 'image/webp' }));
    src.value = objectUrl;
  } catch {
    err.value = tr('Failed to load the problem.');
  }
}

watch(() => [props.problemId, props.bankId, appTheme.value], load, { immediate: true });
onBeforeUnmount(revoke);
</script>

<template>
  <div>
    <p v-if="err" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(err) }}</p>
    <img v-else-if="src" :src="src" alt="" draggable="false"
         class="w-full rounded-lg border border-slate-200 dark:border-slate-800 select-none pointer-events-none" />
    <p v-else class="text-sm text-slate-500 dark:text-slate-400">{{ tr('Loading problem…') }}</p>
  </div>
</template>
