<script setup>
import { tr } from '../lib/i18n';
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { withBase } from '../lib/base';

const auth = useAuth();
const tokens = ref(null);      // null = not available (students, or the server switched it off)
const error = ref('');
const name = ref('');
const password = ref('');
const busy = ref(false);
const fresh = ref(null);       // { token } shown once
const copied = ref(false);

const endpoint = computed(() => `${window.location.origin}${withBase('/mcp')}`);
const command = computed(() => `claude mcp add --transport http beecoding ${endpoint.value} --header "Authorization: Bearer ${fresh.value?.token || '<token>'}"`);
const when = (d) => (d ? new Date(d.endsWith('Z') ? d : d + 'Z').toLocaleDateString() : '—');

async function load() {
  try { tokens.value = await api.get('/api/me/api-tokens'); } catch { tokens.value = null; }
}
onMounted(() => { if (auth.user?.role === 'Teacher') load(); });

async function create() {
  error.value = ''; busy.value = true;
  try {
    fresh.value = await api.post('/api/me/api-tokens', { name: name.value, password: password.value });
    name.value = ''; password.value = ''; copied.value = false;
    await load();
  } catch (e) { error.value = e.message; } finally { busy.value = false; }
}
async function revoke(t) {
  error.value = '';
  try { await api.del(`/api/me/api-tokens/${t.id}`); fresh.value = null; await load(); }
  catch (e) { error.value = e.message; }
}
async function copy() {
  try { await navigator.clipboard.writeText(command.value); copied.value = true; } catch { /* the text is selectable */ }
}
</script>

<template>
  <section v-if="tokens" class="space-y-3">
    <h2 class="font-semibold text-sm">{{ tr('Connect an AI assistant') }}</h2>
    <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
      <p class="text-sm text-slate-500 dark:text-slate-400">
        {{ tr('A token lets an AI assistant (for example Claude through MCP) read the progress of the boards you teach: names and counts only, never code or statements. It can only read, and only what you can see. Revoke it here at any time.') }}
      </p>
      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(error) }}</p>

      <div v-if="fresh" class="rounded-lg bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 p-3 space-y-2">
        <p class="text-sm font-medium">{{ tr('Copy this now — it is shown only once.') }}</p>
        <pre class="text-xs font-mono whitespace-pre-wrap break-all select-all">{{ command }}</pre>
        <button @click="copy" class="row-action-btn row-action-btn--accent">{{ copied ? tr('Copied') : tr('Copy command') }}</button>
      </div>

      <ul v-if="tokens.length" class="divide-y divide-slate-100 dark:divide-slate-800">
        <li v-for="t in tokens" :key="t.id" class="py-2 flex items-center gap-2 flex-wrap text-sm">
          <span class="font-medium">{{ t.name }}</span>
          <span class="text-xs text-slate-500 dark:text-slate-400">
            {{ tr('created {a}, expires {b}, last used {c}', { a: when(t.createdAt), b: when(t.expiresAt), c: t.lastUsedAt ? when(t.lastUsedAt) : tr('never') }) }}
          </span>
          <button @click="revoke(t)" class="ml-auto row-action-btn row-action-btn--danger">{{ tr('Revoke') }}</button>
        </li>
      </ul>

      <div v-if="tokens.length < 5" class="flex flex-wrap gap-2">
        <input v-model="name" maxlength="80" :placeholder="tr('Token name, e.g. My laptop')" :aria-label="tr('Token name, e.g. My laptop')"
               class="flex-1 min-w-[10rem] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <input v-model="password" type="password" autocomplete="current-password" :placeholder="tr('Password to confirm')" :aria-label="tr('Password to confirm')" @keyup.enter="create"
               class="flex-1 min-w-[10rem] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="create" :disabled="busy || !name || !password" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">{{ tr('Create token') }}</button>
      </div>
    </div>
  </section>
</template>
