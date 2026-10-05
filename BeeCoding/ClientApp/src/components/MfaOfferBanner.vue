<script setup>
import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useAuth } from '../stores/auth';

// Soft offer to turn on two-step verification, shown to a signed-in user who has no second factor.
// "Not now" hides it for two weeks; "Don't ask again" hides it for good (per user, per browser).
const auth = useAuth();
const router = useRouter();
const SNOOZE_MS = 14 * 24 * 60 * 60 * 1000;
const key = computed(() => `beecoding.mfaOffer.${auth.user?.id}`);

const read = () => { try { return Number(localStorage.getItem(key.value)) || 0; } catch { return 0; } };
const hiddenUntil = ref(read());
const visible = computed(() => hiddenUntil.value < Date.now());

function hide(until) {
  hiddenUntil.value = until;
  try { localStorage.setItem(key.value, String(until)); } catch { /* ignore */ }
}
const notNow = () => hide(Date.now() + SNOOZE_MS);
const never = () => hide(Number.MAX_SAFE_INTEGER);
const setUp = () => router.push({ path: '/account', hash: '#two-step' });
</script>

<template>
  <div v-if="visible" class="shrink-0 bg-sky-50 dark:bg-sky-500/10 border-b border-sky-200 dark:border-sky-500/20 text-sm text-sky-900 dark:text-sky-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span>
        🔒 Protect your account with two-step verification
        <span class="hidden sm:inline">— {{ auth.isTeacher ? 'your boards and your students’ work depend on it.' : 'a stolen password alone will no longer be enough.' }}</span>
      </span>
      <button @click="setUp" class="font-semibold underline hover:no-underline">Set it up</button>
      <span class="ml-auto flex items-center gap-3 text-xs text-sky-700 dark:text-sky-300">
        <button @click="notNow" class="hover:underline">Not now</button>
        <button @click="never" class="hover:underline">Don't ask again</button>
      </span>
    </div>
  </div>
</template>
