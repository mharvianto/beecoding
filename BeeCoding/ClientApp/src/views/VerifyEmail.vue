<script setup>
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import ThemeToggle from '../components/ThemeToggle.vue';

// Two jobs: (1) the target of the emailed link (?token=…), which works signed-in or not;
// (2) the holding screen for an unverified user when the server requires verified emails.
const route = useRoute();
const router = useRouter();
const auth = useAuth();
const token = String(route.query.token || '');

const state = ref(token ? 'checking' : 'waiting');   // checking | done | failed | waiting
const error = ref('');
const busy = ref(false);
const sent = ref(false);

onMounted(async () => {
  if (!token) {
    if (auth.user?.emailVerified) router.replace('/boards');
    return;
  }
  try {
    await api.post('/api/auth/verify-email', { token });
    state.value = 'done';
    if (auth.user) await auth.fetchMe();   // refresh the verified flag in the open session
  } catch (e) {
    state.value = 'failed';
    error.value = e.message;
  }
});

async function resend() {
  busy.value = true; error.value = '';
  try { await api.post('/api/auth/resend-verification'); sent.value = true; }
  catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}
async function refresh() {
  await auth.fetchMe();
  if (auth.user?.emailVerified) router.replace('/boards');
  else error.value = 'Not verified yet — open the link in the email first.';
}
async function signOut() { await auth.logout(); router.replace('/login'); }
</script>

<template>
  <div class="min-h-[80vh] relative flex items-center justify-center px-4">
    <div class="absolute right-4 top-4"><ThemeToggle /></div>
    <div class="max-w-sm w-full">
      <h1 class="text-2xl font-bold text-amber-600 dark:text-amber-400 mb-1">🐝 BeeCoding</h1>
      <p class="text-slate-500 dark:text-slate-400 mb-6 text-sm">Confirm your email</p>

      <p v-if="state === 'checking'" class="text-sm text-slate-400 dark:text-slate-500">Checking your link…</p>

      <div v-else-if="state === 'done'" class="space-y-3">
        <p class="text-sm bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300 rounded-lg px-3 py-3">
          Thanks — your email address is confirmed.
        </p>
        <RouterLink :to="auth.user ? '/boards' : '/login'" class="inline-block bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium">
          {{ auth.user ? 'Continue' : 'Sign in' }}
        </RouterLink>
      </div>

      <div v-else-if="state === 'failed'" class="space-y-3">
        <p class="text-sm bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-300 rounded-lg px-3 py-3">{{ error }}</p>
        <p class="text-sm text-slate-500 dark:text-slate-400">
          <template v-if="auth.user">Request a new link below.</template>
          <template v-else>Sign in and request a new link from the banner at the top.</template>
        </p>
        <button v-if="auth.user" @click="resend" :disabled="busy" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
          {{ sent ? 'Sent ✓' : busy ? '…' : 'Resend email' }}
        </button>
        <RouterLink v-else to="/login" class="inline-block text-sm text-amber-600 dark:text-amber-400">Go to sign in</RouterLink>
      </div>

      <div v-else class="space-y-3">
        <p class="text-sm text-slate-600 dark:text-slate-300">
          We sent a confirmation link to <span class="font-semibold">{{ auth.user?.email }}</span>.
          Open it to start using BeeCoding. Check your spam folder if it doesn't show up.
        </p>
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
        <div class="flex flex-wrap gap-2">
          <button @click="resend" :disabled="busy || sent" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
            {{ sent ? 'Sent ✓' : busy ? '…' : 'Resend email' }}
          </button>
          <button @click="refresh" class="border border-slate-300 dark:border-slate-700 rounded-lg px-4 py-2 text-sm font-medium">I've confirmed it</button>
          <button @click="signOut" class="text-sm text-slate-500 dark:text-slate-400 px-2">Sign out</button>
        </div>
      </div>
    </div>
  </div>
</template>
