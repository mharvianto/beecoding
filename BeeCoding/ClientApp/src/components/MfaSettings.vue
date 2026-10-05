<script setup>
import { ref, computed, onMounted } from 'vue';
import { useAuth } from '../stores/auth';
import { api } from '../lib/api';
import QrCode from './QrCode.vue';

const auth = useAuth();
const status = ref(null);          // { totpEnabled, recoveryCodesLeft }
const error = ref('');
const busy = ref(false);

const setup = ref(null);           // { secret, uri } while an authenticator app is being set up
const setupCode = ref('');
const secretCopied = ref(false);

const pending = ref(null);         // { kind: 'totp' | 'codes' } waiting for the password
const password = ref('');

const codes = ref(null);           // freshly issued recovery codes, shown once
const codesCopied = ref(false);

const hasFactor = computed(() => !!status.value?.totpEnabled);

async function load() {
  try { status.value = await api.get('/api/auth/mfa'); } catch (e) { error.value = e.message; return; }
  // keep the "turn on two-step" offer in the header in step with reality
  if (auth.user && auth.user.mfaEnabled !== hasFactor.value) auth.user.mfaEnabled = hasFactor.value;
}
onMounted(load);

async function guarded(fn) {
  error.value = '';
  busy.value = true;
  try { await fn(); } catch (e) { error.value = e.message; } finally { busy.value = false; }
}

// ---- authenticator app ----
const startSetup = () => guarded(async () => {
  setup.value = await api.post('/api/auth/mfa/totp/setup');
  setupCode.value = '';
});
const cancelSetup = () => { setup.value = null; setupCode.value = ''; error.value = ''; };
const enableTotp = () => guarded(async () => {
  const r = await api.post('/api/auth/mfa/totp/enable', { code: setupCode.value });
  setup.value = null;
  if (r.recoveryCodes) codes.value = r.recoveryCodes;
  await load();
});
async function copySecret() {
  try { await navigator.clipboard.writeText(setup.value.secret); secretCopied.value = true; setTimeout(() => (secretCopied.value = false), 1500); } catch { /* ignore */ }
}

// ---- actions that need the password ----
function ask(kind) { pending.value = { kind }; password.value = ''; error.value = ''; }
const confirmPending = () => guarded(async () => {
  const { kind } = pending.value;
  if (kind === 'totp') await api.post('/api/auth/mfa/totp/disable', { password: password.value });
  else codes.value = (await api.post('/api/auth/mfa/recovery-codes', { password: password.value })).recoveryCodes;
  pending.value = null;
  await load();
});
const pendingLabel = computed(() => ({
  totp: 'Remove authenticator app', codes: 'Generate new codes',
}[pending.value?.kind] || ''));

// ---- recovery codes ----
async function copyCodes() {
  try { await navigator.clipboard.writeText(codes.value.join('\n')); codesCopied.value = true; setTimeout(() => (codesCopied.value = false), 1500); } catch { /* ignore */ }
}
function downloadCodes() {
  const blob = new Blob([`BeeCoding recovery codes\nEach code works once.\n\n${codes.value.join('\n')}\n`], { type: 'text/plain' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'beecoding-recovery-codes.txt';
  a.click();
  URL.revokeObjectURL(a.href);
}

const inputCls = 'border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm';
const primaryBtn = 'bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50';
</script>

<template>
  <section id="two-step" class="space-y-3 scroll-mt-4">
    <h2 class="font-semibold text-sm">Two-step verification</h2>
    <p class="text-sm text-slate-500 dark:text-slate-400 max-w-xl">
      After your password, sign-in also asks for a code from an authenticator app, so a stolen password alone is not enough.
    </p>
    <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>

    <div v-if="status" class="space-y-3 max-w-xl">
      <!-- authenticator app -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-3">
        <div class="flex items-center gap-2">
          <div class="font-medium text-sm">Authenticator app</div>
          <span v-if="status.totpEnabled" class="text-[11px] px-1.5 py-0.5 rounded-full bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">on</span>
          <button v-if="status.totpEnabled" @click="ask('totp')" class="ml-auto row-action-btn row-action-btn--danger">Remove</button>
          <button v-else-if="!setup" @click="startSetup" :disabled="busy" :class="['ml-auto', primaryBtn]">Set up</button>
        </div>
        <p v-if="!status.totpEnabled && !setup" class="text-sm text-slate-500 dark:text-slate-400">
          Use an app such as Google Authenticator, Microsoft Authenticator, 1Password or Authy to generate sign-in codes.
        </p>
        <div v-if="setup" class="space-y-3">
          <p class="text-sm text-slate-600 dark:text-slate-300">1. Scan this QR code with your authenticator app.</p>
          <QrCode :text="setup.uri" :size="180" />
          <p class="text-sm text-slate-600 dark:text-slate-300">
            Can't scan? Enter this key by hand:
            <code class="font-mono text-xs bg-slate-100 dark:bg-slate-800 rounded px-1.5 py-0.5 select-all break-all">{{ setup.secret }}</code>
            <button @click="copySecret" class="text-xs text-amber-600 dark:text-amber-400 ml-1">{{ secretCopied ? 'copied' : 'copy' }}</button>
          </p>
          <p class="text-sm text-slate-600 dark:text-slate-300">2. Enter the 6-digit code it shows to turn it on.</p>
          <div class="flex flex-wrap gap-2">
            <input v-model="setupCode" inputmode="numeric" autocomplete="one-time-code" maxlength="7" placeholder="123456"
                   @keyup.enter="enableTotp" :class="[inputCls, 'w-36 tracking-widest text-center']" />
            <button @click="enableTotp" :disabled="busy || !setupCode" :class="primaryBtn">Turn on</button>
            <button @click="cancelSetup" class="text-sm text-slate-500 dark:text-slate-400 px-2">Cancel</button>
          </div>
        </div>
      </div>

      <!-- recovery codes -->
      <div v-if="hasFactor" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-2">
        <div class="flex items-center gap-2">
          <div class="font-medium text-sm">Recovery codes</div>
          <button @click="ask('codes')" class="ml-auto row-action-btn">Generate new codes</button>
        </div>
        <p class="text-sm text-slate-500 dark:text-slate-400">
          If you lose your device, one of these codes lets you in. Each works once.
          <span :class="status.recoveryCodesLeft <= 2 ? 'text-red-600 dark:text-red-400 font-medium' : ''">{{ status.recoveryCodesLeft }} left.</span>
        </p>
      </div>
    </div>

    <!-- password confirmation -->
    <div v-if="pending" class="max-w-xl border border-amber-200 dark:border-amber-500/30 bg-amber-50/50 dark:bg-amber-500/5 rounded-xl p-4 space-y-2">
      <div class="text-sm font-medium">{{ pendingLabel }}</div>
      <p class="text-sm text-slate-500 dark:text-slate-400">Enter your password to confirm.</p>
      <div class="flex flex-wrap gap-2">
        <input v-model="password" type="password" autocomplete="current-password" placeholder="Password" @keyup.enter="confirmPending" :class="[inputCls, 'flex-1 min-w-[10rem]']" />
        <button @click="confirmPending" :disabled="busy || !password" :class="primaryBtn">Confirm</button>
        <button @click="pending = null" class="text-sm text-slate-500 dark:text-slate-400 px-2">Cancel</button>
      </div>
    </div>

    <!-- recovery codes, shown once -->
    <div v-if="codes" class="max-w-xl border border-emerald-200 dark:border-emerald-500/30 bg-emerald-50/50 dark:bg-emerald-500/5 rounded-xl p-4 space-y-3">
      <div class="text-sm font-medium">Save your recovery codes</div>
      <p class="text-sm text-slate-600 dark:text-slate-300">Keep them somewhere safe. They are shown only now, and each works once.</p>
      <div class="grid grid-cols-2 gap-x-6 gap-y-1 font-mono text-sm">
        <span v-for="c in codes" :key="c" class="select-all">{{ c }}</span>
      </div>
      <div class="flex flex-wrap gap-2">
        <button @click="copyCodes" class="row-action-btn">{{ codesCopied ? 'Copied' : 'Copy' }}</button>
        <button @click="downloadCodes" class="row-action-btn">Download</button>
        <button @click="codes = null" :class="['ml-auto', primaryBtn]">I saved them</button>
      </div>
    </div>
  </section>
</template>
