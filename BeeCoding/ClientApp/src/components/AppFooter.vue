<script setup>
const year = new Date().getFullYear();
</script>

<template>
  <footer class="relative shrink-0 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900">
    <div class="max-w-6xl mx-auto px-4 py-2.5 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-xs text-slate-400 dark:text-slate-500">
      <span>© {{ year }} BeeCoding</span>
      <nav class="flex items-center gap-4">
        <RouterLink to="/privacy" class="hover:text-slate-700 dark:hover:text-slate-300">Privacy Policy</RouterLink>
        <RouterLink to="/terms" class="hover:text-slate-700 dark:hover:text-slate-300">Terms &amp; Agreement</RouterLink>
      </nav>
    </div>
  </footer>
</template>
