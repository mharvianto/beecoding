<script setup>
import { ref } from 'vue';
import { api } from '../lib/api';
import ThemeToggle from '../components/ThemeToggle.vue';

const email = ref('');
const busy = ref(false);
const sent = ref(false);
const error = ref('');

async function submit() {
  error.value = '';
  busy.value = true;
  try {
    await api.post('/api/auth/forgot-password', { email: email.value });
    sent.value = true;   // same message whether or not the address has an account
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
      <p class="text-slate-500 dark:text-slate-400 mb-6 text-sm">Reset your password</p>

      <div v-if="sent" class="text-sm bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300 rounded-lg px-3 py-3">
        If an account exists for <span class="font-semibold">{{ email }}</span>, we've emailed a link to reset the password.
        It works once and expires in an hour. Check your spam folder if it doesn't show up.
      </div>
      <form v-else @submit.prevent="submit" class="space-y-3">
        <p class="text-sm text-slate-500 dark:text-slate-400">Enter your account email and we'll send you a link to choose a new password.</p>
        <input v-model="email" type="email" placeholder="Email" required autofocus
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
        <button :disabled="busy"
                class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
          {{ busy ? '…' : 'Send reset link' }}
        </button>
      </form>
      <p class="text-sm text-slate-500 dark:text-slate-400 mt-4">
        <RouterLink to="/login" class="text-amber-600 dark:text-amber-400">&larr; Back to sign in</RouterLink>
      </p>
    </div>
  </div>
</template>
