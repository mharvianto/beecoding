<script setup>
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import GoogleButton from './GoogleButton.vue';

const auth = useAuth();
const route = useRoute();
const status = ref(null);      // { linked, email }
const error = ref(route.query.googleError ? String(route.query.googleError) : '');
const msg = ref(route.query.google === 'linked' ? 'Google account connected.' : '');
const asking = ref(false);
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
    msg.value = 'Google account disconnected.';
    status.value = await api.get('/api/auth/google/status');
  } catch (e) { error.value = e.message; } finally { busy.value = false; }
}
</script>

<template>
  <section v-if="status && (auth.config.google || status.linked)" class="space-y-3">
    <h2 class="font-semibold text-sm">Connected accounts</h2>
    <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
    <p v-if="msg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ msg }}</p>
    <div class="max-w-xl border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
      <div class="flex items-center gap-2 flex-wrap">
        <div class="font-medium text-sm">Google</div>
        <span v-if="status.linked" class="text-xs text-slate-500 dark:text-slate-400 truncate">{{ status.email }}</span>
        <span v-if="status.linked" class="text-[11px] px-1.5 py-0.5 rounded-full bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">connected</span>
        <button v-if="status.linked && !asking" @click="asking = true; error = ''; msg = ''" class="ml-auto row-action-btn row-action-btn--danger">Disconnect</button>
        <div v-else-if="!status.linked && auth.config.google" class="ml-auto w-48"><GoogleButton mode="link" label="Connect Google" /></div>
      </div>
      <p v-if="!status.linked" class="text-sm text-slate-500 dark:text-slate-400">Connect Google to sign in with one click.</p>
      <div v-if="asking" class="flex flex-wrap gap-2">
        <input v-model="password" type="password" autocomplete="current-password" placeholder="Password to confirm" @keyup.enter="unlink"
               class="flex-1 min-w-[10rem] border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="unlink" :disabled="busy || !password" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">Disconnect</button>
        <button @click="asking = false; password = ''" class="text-sm text-slate-500 dark:text-slate-400 px-2">Cancel</button>
      </div>
    </div>
  </section>
</template>
