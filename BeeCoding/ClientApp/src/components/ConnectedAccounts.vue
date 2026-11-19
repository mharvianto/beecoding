<script setup>
import { tr } from '../lib/i18n';
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { withBase } from '../lib/base';

const auth = useAuth();
const route = useRoute();
const status = ref(null);      // { linked, email }
const error = ref(route.query.googleError ? String(route.query.googleError) : '');
const msg = ref(route.query.google === 'linked' ? 'Google account connected.' : '');
const asking = ref(false);
const connecting = ref(false);   // asking for the password before the Google round trip
const password = ref('');
const busy = ref(false);

onMounted(async () => {
  await auth.loadConfig();
  try { status.value = await api.get('/api/auth/google/status'); } catch { /* hidden */ }
});

async function unlink() {
  error.value = ''; busy.value = true;
  try {
    await api.post('/api/auth/google/unlink', { password: password.value });
    asking.value = false; password.value = '';
    msg.value = tr('Google account disconnected.');
    status.value = await api.get('/api/auth/google/status');
  } catch (e) { error.value = e.message; } finally { busy.value = false; }
}

// Adding a sign-in method needs the password, like removing one: the server hands back a short-lived ticket for the redirect.
async function connect() {
  error.value = ''; busy.value = true;
  try {
    const { ticket } = await api.post('/api/auth/google/link-ticket', { password: password.value });
    window.location.href = withBase('/api/auth/google/start') + `?mode=link&ticket=${encodeURIComponent(ticket)}`;
  } catch (e) { error.value = e.message; busy.value = false; }
}
</script>

<template>
  <section v-if="status && (auth.config.google || status.linked)" class="space-y-3">
    <h2 class="font-semibold text-sm">{{ tr('Connected accounts') }}</h2>
    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(error) }}</p>
    <p v-if="msg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ tr(msg) }}</p>
    <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
      <div class="flex items-center gap-2 flex-wrap">
        <div class="font-medium text-sm">Google</div>
        <span v-if="status.linked" class="text-xs text-slate-500 dark:text-slate-400 truncate">{{ status.email }}</span>
        <span v-if="status.linked" class="text-[11px] px-1.5 py-0.5 rounded-full bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">{{ tr('connected') }}</span>
        <button v-if="status.linked && !asking" @click="asking = true; error = ''; msg = ''" class="ml-auto row-action-btn row-action-btn--danger">{{ tr('Disconnect') }}</button>
        <button v-else-if="!status.linked && auth.config.google && !connecting" @click="connecting = true; error = ''; msg = ''" class="ml-auto bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium">{{ tr('Connect Google') }}</button>
      </div>
      <p v-if="!status.linked" class="text-sm text-slate-500 dark:text-slate-400">{{ tr('Connect Google to sign in with one click.') }}</p>
      <div v-if="connecting" class="flex flex-wrap gap-2">
        <input v-model="password" type="password" autocomplete="current-password" :placeholder="tr('Password to confirm')" :aria-label="tr('Password to confirm')" @keyup.enter="connect"
               class="flex-1 min-w-[10rem] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="connect" :disabled="busy || !password" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">{{ tr('Continue to Google') }}</button>
        <button @click="connecting = false; password = ''" class="text-sm text-slate-500 dark:text-slate-400 px-2">{{ tr('Cancel') }}</button>
      </div>
      <div v-if="asking" class="flex flex-wrap gap-2">
        <input v-model="password" type="password" autocomplete="current-password" :placeholder="tr('Password to confirm')" :aria-label="tr('Password to confirm')" @keyup.enter="unlink"
               class="flex-1 min-w-[10rem] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="unlink" :disabled="busy || !password" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">{{ tr('Disconnect') }}</button>
        <button @click="asking = false; password = ''" class="text-sm text-slate-500 dark:text-slate-400 px-2">{{ tr('Cancel') }}</button>
      </div>
    </div>
  </section>
</template>
