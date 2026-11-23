<script setup>
import { tr } from '../lib/i18n';
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '../lib/api';

// Where an AI assistant (claude.ai) sends a teacher to say yes or no (OAuth authorization, see OAuthController).
const q = useRoute().query;
const info = ref(null);
const error = ref('');
const busy = ref(false);

onMounted(async () => {
  try { info.value = await api.get(`/api/oauth/client?clientId=${encodeURIComponent(q.client_id || '')}&redirectUri=${encodeURIComponent(q.redirect_uri || '')}`); }
  catch { error.value = tr('This connection request is not valid. Start again from the assistant.'); }
});

async function decide(approve) {
  busy.value = true; error.value = '';
  try {
    const r = await api.post('/api/oauth/authorize', {
      clientId: q.client_id, redirectUri: q.redirect_uri, state: q.state || null,
      codeChallenge: q.code_challenge || '', codeChallengeMethod: q.code_challenge_method || null,
      resource: q.resource || null, approve,
    });
    window.location.href = r.redirectTo;   // back to the assistant, which finishes the sign-in
  } catch (e) { error.value = e.message; busy.value = false; }
}
</script>

<template>
  <div class="max-w-md mx-auto p-6 space-y-4">
    <h1 class="text-lg font-bold">{{ tr('Connect an AI assistant') }}</h1>
    <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
    <div v-if="info" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
      <template v-if="info.teacher">
        <p class="text-sm">{{ tr('{name} ({host}) asks to connect to your BeeCoding account.', { name: info.name, host: info.host }) }}</p>
        <ul class="text-sm list-disc pl-5 space-y-1 text-slate-600 dark:text-slate-300">
          <li>{{ tr('It can read the progress of the boards you teach: names and counts only.') }}</li>
          <li>{{ tr('It cannot see code or statements, and cannot change anything.') }}</li>
          <li>{{ tr('You can disconnect it any time under Account → Connect an AI assistant.') }}</li>
        </ul>
        <p class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Only continue if you started this from the assistant yourself.') }}</p>
        <div class="flex gap-2">
          <button @click="decide(true)" :disabled="busy" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">{{ tr('Allow') }}</button>
          <button @click="decide(false)" :disabled="busy" class="text-sm text-slate-500 dark:text-slate-400 px-3">{{ tr('Deny') }}</button>
        </div>
      </template>
      <p v-else class="text-sm">{{ tr('Only teacher accounts can connect an assistant.') }}</p>
    </div>
  </div>
</template>
