<script setup>
import { useRouter } from 'vue-router';
import { useAuth } from '../stores/auth';

// An organization admin invited this account; nothing happens until the user answers (Account > Profile > Organizations).
const auth = useAuth();
const router = useRouter();
</script>

<template>
  <div class="shrink-0 bg-amber-50 dark:bg-amber-500/10 border-b border-amber-200 dark:border-amber-500/20 text-sm text-amber-900 dark:text-amber-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span>🏫 You have {{ auth.user.pendingOrgInvites }} organization invitation{{ auth.user.pendingOrgInvites === 1 ? '' : 's' }} waiting.</span>
      <button @click="router.push({ path: '/account/profile', hash: '#organizations' })" class="font-semibold underline hover:no-underline">Review</button>
    </div>
  </div>
</template>
