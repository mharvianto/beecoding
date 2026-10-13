<script setup>
import { computed, onMounted } from 'vue';
import { useAuth } from '../stores/auth';

const year = new Date().getFullYear();
const auth = useAuth();
onMounted(() => auth.loadConfig());

// Which build is running: short commit hash + when it was built, in the viewer's own time zone.
const build = computed(() => {
  const { build: hash, builtAt } = auth.config;
  if (!hash) return '';
  const when = builtAt ? new Date(builtAt).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '';
  return when ? `${hash} · ${when}` : hash;
});
</script>

<template>
  <footer class="relative shrink-0 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900">
    <div class="max-w-7xl mx-auto px-4 py-2.5 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-xs text-slate-400 dark:text-slate-500">
      <span>© {{ year }} BeeCoding<span v-if="build" class="ml-2" title="Build (commit hash · build time)">build {{ build }}</span></span>
      <nav class="flex items-center gap-4">
        <RouterLink to="/privacy" class="hover:text-slate-700 dark:hover:text-slate-300">Privacy Policy</RouterLink>
        <RouterLink to="/terms" class="hover:text-slate-700 dark:hover:text-slate-300">Terms &amp; Agreement</RouterLink>
      </nav>
    </div>
  </footer>
</template>
