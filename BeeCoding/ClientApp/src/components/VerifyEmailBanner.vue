<script setup>
import { ref } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';

// Soft nudge shown to a signed-in user whose email isn't confirmed yet (only when the server can send mail).
const auth = useAuth();
const busy = ref(false);
const sent = ref(false);
const error = ref('');

async function resend() {
  busy.value = true; error.value = '';
  try {
    await api.post('/api/auth/resend-verification');
    sent.value = true;
  } catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}
</script>

<template>
  <div class="shrink-0 bg-amber-50 dark:bg-amber-500/10 border-b border-amber-200 dark:border-amber-500/20 text-sm text-amber-900 dark:text-amber-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span>📧 Please confirm your email address — we sent a link to <span class="font-semibold">{{ auth.user.email }}</span>.</span>
      <span v-if="sent" class="text-emerald-700 dark:text-emerald-300">Sent ✓ — check your inbox (and spam).</span>
      <button v-else @click="resend" :disabled="busy" class="underline hover:no-underline disabled:opacity-50">{{ busy ? 'Sending…' : 'Resend email' }}</button>
      <span v-if="error" class="text-red-600 dark:text-red-400">{{ error }}</span>
    </div>
  </div>
</template>
