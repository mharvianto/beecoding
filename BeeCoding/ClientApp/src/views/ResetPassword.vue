<script setup>
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import ThemeToggle from '../components/ThemeToggle.vue';

const route = useRoute();
const router = useRouter();
const token = String(route.query.token || '');

const checking = ref(true);
const invalid = ref('');
const password = ref('');
const confirm = ref('');
const error = ref('');
const busy = ref(false);

onMounted(async () => {
  try {
    if (!token) throw new Error('This reset link is invalid or has expired.');
    await api.get(`/api/auth/reset-password/check?token=${encodeURIComponent(token)}`);
  } catch (e) {
    invalid.value = e.message;
  } finally {
    checking.value = false;
  }
});

async function submit() {
  error.value = '';
  if (password.value !== confirm.value) { error.value = 'The two passwords don\'t match.'; return; }
  busy.value = true;
  try {
    await api.post('/api/auth/reset-password', { token, newPassword: password.value });
    router.replace({ path: '/login', query: { reset: '1' } });
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="min-h-[80vh] relative flex items-center justify-center px-4">
    <div class="absolute right-4 top-4"><ThemeToggle /></div>
    <div class="max-w-sm w-full">
      <h1 class="text-2xl font-bold text-amber-600 dark:text-amber-400 mb-1">🐝 BeeCoding</h1>
      <p class="text-slate-500 dark:text-slate-400 mb-6 text-sm">Choose a new password</p>

      <p v-if="checking" class="text-sm text-slate-400 dark:text-slate-500">Checking your link…</p>
      <div v-else-if="invalid" class="space-y-3">
        <p class="text-sm bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-300 rounded-lg px-3 py-3">{{ invalid }}</p>
        <RouterLink to="/forgot-password" class="inline-block text-sm text-amber-600 dark:text-amber-400">Request a new link</RouterLink>
      </div>
      <form v-else @submit.prevent="submit" class="space-y-3">
        <input v-model="password" type="password" placeholder="New password (min 8 chars)" required minlength="8" autofocus autocomplete="new-password"
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <input v-model="confirm" type="password" placeholder="Repeat the new password" required autocomplete="new-password"
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
        <button :disabled="busy"
                class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
          {{ busy ? '…' : 'Set new password' }}
        </button>
      </form>
      <p class="text-sm text-slate-500 dark:text-slate-400 mt-4">
        <RouterLink to="/login" class="text-amber-600 dark:text-amber-400">&larr; Back to sign in</RouterLink>
      </p>
    </div>
  </div>
</template>
