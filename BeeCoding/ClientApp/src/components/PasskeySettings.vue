<script setup>
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import { createPasskey, passkeysSupported } from '../lib/webauthn';

// Passkeys let the user sign in without typing an email or password (see PasskeyController).
const status = ref(null);          // { available, passkeys: [{ id, name, createdAt, lastUsedAt }] }
const error = ref('');
const busy = ref(false);
const name = ref('');
const renaming = ref(null);        // { id, name }
const removing = ref(null);        // passkey id waiting for the password
const password = ref('');

const supported = computed(() => passkeysSupported());

async function load() {
  try { status.value = await api.get('/api/auth/passkeys'); } catch (e) { error.value = e.message; }
}
onMounted(load);

async function guarded(fn) {
  error.value = '';
  busy.value = true;
  try { await fn(); } catch (e) { error.value = e.message; } finally { busy.value = false; }
}

const add = () => guarded(async () => {
  const { options, state } = await api.post('/api/auth/passkeys/options');
  const response = await createPasskey(options);
  await api.post('/api/auth/passkeys', { state, name: name.value, response });
  name.value = '';
  await load();
});
const saveRename = () => guarded(async () => {
  await api.patch(`/api/auth/passkeys/${renaming.value.id}`, { name: renaming.value.name });
  renaming.value = null;
  await load();
});
const confirmRemove = () => guarded(async () => {
  await api.del(`/api/auth/passkeys/${removing.value}`, { password: password.value });
  removing.value = null; password.value = '';
  await load();
});

const when = (iso) => new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleDateString();
const inputCls = 'border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm';
const primaryBtn = 'bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50';
</script>

<template>
  <section v-if="status?.available" id="passkeys" class="space-y-3 scroll-mt-4">
    <h2 class="font-semibold text-sm">Passkeys</h2>
    <p class="text-sm text-slate-500 dark:text-slate-400">
      Sign in with your fingerprint, face or device PIN, with no email or password to type. A passkey stays on your
      device, can't be phished, and is already two-factor, so it skips the authenticator-app step.
    </p>
    <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>

    <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
      <p v-if="!supported" class="text-sm text-slate-500 dark:text-slate-400">
        This browser can't use passkeys here. Passkeys need a secure (HTTPS) connection and a browser that supports WebAuthn.
      </p>
      <ul v-if="status.passkeys.length" class="divide-y divide-slate-100 dark:divide-slate-800">
        <li v-for="k in status.passkeys" :key="k.id" class="py-2 flex items-center gap-2 flex-wrap">
          <template v-if="renaming?.id === k.id">
            <input v-model="renaming.name" maxlength="80" @keyup.enter="saveRename" :class="[inputCls, 'flex-1 min-w-0']" />
            <button @click="saveRename" :disabled="busy" class="row-action-btn row-action-btn--success">Save</button>
            <button @click="renaming = null" class="row-action-btn">Cancel</button>
          </template>
          <template v-else>
            <div class="min-w-0 flex-1">
              <div class="text-sm font-medium truncate">🔑 {{ k.name }}</div>
              <div class="text-xs text-slate-400 dark:text-slate-500">
                Added {{ when(k.createdAt) }} · {{ k.lastUsedAt ? `last used ${when(k.lastUsedAt)}` : 'not used yet' }}
              </div>
            </div>
            <button @click="renaming = { id: k.id, name: k.name }" class="row-action-btn">Rename</button>
            <button @click="removing = k.id; password = ''; error = ''" class="row-action-btn row-action-btn--danger">Remove</button>
          </template>
        </li>
      </ul>
      <p v-else-if="supported" class="text-sm text-slate-500 dark:text-slate-400">No passkeys yet.</p>

      <div v-if="removing" class="rounded-lg bg-amber-50/60 dark:bg-amber-500/5 border border-amber-200 dark:border-amber-500/30 p-3 space-y-2">
        <p class="text-sm">Enter your password to remove this passkey.</p>
        <div class="flex flex-wrap gap-2">
          <input v-model="password" type="password" autocomplete="current-password" placeholder="Password" @keyup.enter="confirmRemove" :class="[inputCls, 'flex-1 min-w-[10rem]']" />
          <button @click="confirmRemove" :disabled="busy || !password" :class="primaryBtn">Remove</button>
          <button @click="removing = null" class="text-sm text-slate-500 dark:text-slate-400 px-2">Cancel</button>
        </div>
      </div>

      <div v-if="supported" class="flex flex-wrap gap-2">
        <input v-model="name" maxlength="80" placeholder="Name, e.g. My phone" @keyup.enter="add" :class="[inputCls, 'flex-1 min-w-[10rem]']" />
        <button @click="add" :disabled="busy" :class="primaryBtn">Add a passkey</button>
      </div>
    </div>
  </section>
</template>
