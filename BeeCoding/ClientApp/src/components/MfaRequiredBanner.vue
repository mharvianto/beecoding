<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import { useRouter } from 'vue-router';

// Shown while the platform requires two-step verification for this account and it isn't set up yet.
// The router keeps the user on Account > Security until they turn a method on (see router.js).
const router = useRouter();
// Already on that page the route does not change, so scroll to the block as well: the button must always do something visible.
function setUp() {
  router.push('/account/security#two-step');
  document.getElementById('two-step')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}
</script>

<template>
  <div role="region" :aria-label="tr('Notice')" class="shrink-0 bg-red-50 dark:bg-red-500/10 border-b border-red-200 dark:border-red-500/30 text-sm text-red-900 dark:text-red-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span><Icon name="lock" /> {{ tr('Two-step verification is required for your role. Turn on an authenticator app or email code to continue.') }}</span>
      <button @click="setUp" class="font-semibold underline hover:no-underline">{{ tr('Set it up') }}</button>
    </div>
  </div>
</template>
