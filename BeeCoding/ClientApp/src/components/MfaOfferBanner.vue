<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';

// Soft offer to turn on two-step verification, shown to a signed-in user who has no second factor.
// "Not now" hides it for two weeks, "Don't ask again" for good. The choice lives on the server
// (User.MfaOfferHiddenUntil, delivered in /api/auth/me) so it follows the user to every device.
const auth = useAuth();
const router = useRouter();

// Older builds kept the choice in localStorage; hand an unexpired one to the server once, then forget it.
onMounted(async () => {
  const key = `beecoding.mfaOffer.${auth.user?.id}`;
  let until = 0;
  try { until = Number(localStorage.getItem(key)) || 0; localStorage.removeItem(key); } catch { /* ignore */ }
  if (until > Date.now() && !auth.user.mfaOfferSnoozed) await hide(until >= Number.MAX_SAFE_INTEGER / 2);
});

async function hide(forever) {
  auth.user.mfaOfferSnoozed = true;   // hide at once; the server call just makes it stick
  try { await api.post('/api/auth/mfa/offer/snooze', { forever }); } catch { /* it reappears next load */ }
}
const setUp = () => router.push({ path: '/account/security', hash: '#two-step' });
</script>

<template>
  <div v-if="!auth.user.mfaOfferSnoozed" role="region" :aria-label="tr('Notice')" class="shrink-0 bg-sky-50 dark:bg-sky-500/10 border-b border-sky-200 dark:border-sky-500/20 text-sm text-sky-900 dark:text-sky-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span>
        <Icon name="lock" /> {{ tr('Protect your account with two-step verification') }}
        <span class="hidden sm:inline">— {{ auth.isTeacher ? tr('your boards and your students’ work depend on it.') : tr('a stolen password alone will no longer be enough.') }}</span>
      </span>
      <button @click="setUp" class="font-semibold underline hover:no-underline">{{ tr('Set it up') }}</button>
      <span class="ml-auto flex items-center gap-3 text-xs text-sky-700 dark:text-sky-300">
        <button @click="hide(false)" class="hover:underline">{{ tr('Not now') }}</button>
        <button @click="hide(true)" class="hover:underline">{{ tr('Don\'t ask again') }}</button>
      </span>
    </div>
  </div>
</template>
