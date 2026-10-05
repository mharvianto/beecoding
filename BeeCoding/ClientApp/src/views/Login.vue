<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import { useRouter, useRoute } from 'vue-router';
import { useAuth } from '../stores/auth';
import ThemeToggle from '../components/ThemeToggle.vue';

const auth = useAuth();
const router = useRouter();
const route = useRoute();
const email = ref('');
const password = ref('');
const error = ref('');
const busy = ref(false);
const canReset = ref(false);   // outgoing email configured on the server
const justReset = route.query.reset === '1';

onMounted(async () => {
  try { canReset.value = (await api.get('/api/auth/config')).passwordReset === true; } catch { /* keep it hidden */ }
});

const challenge = ref(null);   // { ticket, methods } once the password was right but a second factor is needed
const mfaMode = ref('totp');   // 'totp' | 'recovery'
const mfaCode = ref('');

function done() { router.push(route.query.r || '/boards'); }

async function submit() {
  error.value = '';
  busy.value = true;
  try {
    const next = await auth.login(email.value, password.value);
    if (next) {
      challenge.value = next;
      mfaMode.value = next.methods.includes('totp') ? 'totp' : next.methods.includes('passkey') ? 'passkey' : 'recovery';
      mfaCode.value = '';
      if (mfaMode.value === 'passkey') await usePasskey();
      return;
    }
    done();
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
  }
}

async function submitCode() {
  error.value = '';
  busy.value = true;
  try {
    await auth.verifyMfa(challenge.value.ticket, mfaMode.value, mfaCode.value);
    done();
  } catch (e) {
    error.value = e.message;
    if (/expired/i.test(e.message)) backToPassword(false);
  } finally {
    busy.value = false;
  }
}

async function usePasskey() {
  error.value = '';
  busy.value = true;
  try {
    await auth.verifyMfaPasskey(challenge.value.ticket);
    done();
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
  }
}

function backToPassword(clearError = true) {
  challenge.value = null;
  password.value = '';
  mfaCode.value = '';
  if (clearError) error.value = '';
}
</script>

<template>
  <div class="min-h-[80vh] relative flex items-center justify-center px-4">
    <div class="absolute right-4 top-4"><ThemeToggle /></div>
    <div class="max-w-sm w-full">
      <h1 class="text-2xl font-bold text-amber-600 dark:text-amber-400 mb-1">🐝 BeeCoding</h1>
      <p class="text-slate-500 dark:text-slate-400 mb-6 text-sm">{{ challenge ? 'Two-step verification' : 'Sign in to your account' }}</p>
      <p v-if="justReset" class="mb-3 text-sm bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300 rounded-lg px-3 py-2">
        Your password was changed. Sign in with the new one.
      </p>
      <form v-if="challenge" @submit.prevent="submitCode" class="space-y-3">
        <template v-if="mfaMode === 'totp'">
          <p class="text-sm text-slate-600 dark:text-slate-300">Enter the 6-digit code from your authenticator app.</p>
          <input v-model="mfaCode" inputmode="numeric" autocomplete="one-time-code" maxlength="7" placeholder="123456" required autofocus
                 class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 tracking-widest text-center text-lg" />
        </template>
        <template v-else-if="mfaMode === 'recovery'">
          <p class="text-sm text-slate-600 dark:text-slate-300">Enter one of your recovery codes. Each code works once.</p>
          <input v-model="mfaCode" autocomplete="off" maxlength="11" placeholder="xxxxx-xxxxx" required autofocus
                 class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 tracking-wider text-center font-mono" />
        </template>
        <p v-else class="text-sm text-slate-600 dark:text-slate-300">Confirm with your passkey or security key.</p>
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
        <button v-if="mfaMode !== 'passkey'" :disabled="busy || !mfaCode"
                class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
          {{ busy ? '…' : 'Verify' }}
        </button>
        <button v-else type="button" @click="usePasskey" :disabled="busy"
                class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
          {{ busy ? '…' : '🔑 Use passkey' }}
        </button>
        <div class="text-sm flex flex-col gap-1.5 text-amber-600 dark:text-amber-400">
          <button v-if="mfaMode !== 'passkey' && challenge.methods.includes('passkey')" type="button" @click="mfaMode = 'passkey'; error = ''; usePasskey()" class="text-left">Use a passkey instead</button>
          <button v-if="mfaMode !== 'totp' && challenge.methods.includes('totp')" type="button" @click="mfaMode = 'totp'; mfaCode = ''; error = ''" class="text-left">Use my authenticator app</button>
          <button v-if="mfaMode !== 'recovery'" type="button" @click="mfaMode = 'recovery'; mfaCode = ''; error = ''" class="text-left">Use a recovery code</button>
          <button type="button" @click="backToPassword()" class="text-left text-slate-500 dark:text-slate-400">&larr; Back to sign in</button>
        </div>
      </form>
      <form v-else @submit.prevent="submit" class="space-y-3">
        <input v-model="email" type="email" placeholder="Email" required
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <input v-model="password" type="password" placeholder="Password" required
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
        <button :disabled="busy"
                class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
          {{ busy ? '…' : 'Sign in' }}
        </button>
      </form>
      <p v-if="canReset && !challenge" class="text-sm mt-3">
        <RouterLink to="/forgot-password" class="text-amber-600 dark:text-amber-400">Forgot your password?</RouterLink>
      </p>
      <p v-if="!challenge" class="text-sm text-slate-500 dark:text-slate-400 mt-4">
        No account? <RouterLink :to="{ path: '/register', query: route.query.r ? { r: route.query.r } : {} }" class="text-amber-600 dark:text-amber-400">Register</RouterLink>
      </p>
    </div>
  </div>
</template>
