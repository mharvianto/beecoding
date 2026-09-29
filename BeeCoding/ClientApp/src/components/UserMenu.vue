<script setup>
import { ref } from 'vue';
import { useAuth } from '../stores/auth';
import { theme, setTheme } from '../lib/theme';

// Name + role chip collapse into one dropdown holding Account, Theme and Sign out — keeps the
// navbar on a single row even for an admin with every nav link showing.
const emit = defineEmits(['logout']);
const auth = useAuth();
const open = ref(false);

const THEMES = [['light', '☀️', 'Light'], ['dark', '🌙', 'Dark'], ['system', '🖥️', 'System']];
const initial = () => (auth.user?.displayName || '?').trim()[0]?.toUpperCase() || '?';
</script>

<template>
  <div class="relative">
    <button @click="open = !open" class="flex items-center gap-2 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100"
            title="Account menu" aria-label="Account menu">
      <span class="w-7 h-7 rounded-full bg-amber-400 text-white text-xs font-bold grid place-items-center shrink-0 sm:hidden">{{ initial() }}</span>
      <span class="hidden sm:block truncate max-w-[9rem]">{{ auth.user.displayName }}</span>
      <span class="hidden sm:inline px-2 py-0.5 rounded-full text-xs shrink-0"
            :class="auth.isTeacher
              ? 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
              : 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300'">{{ auth.user.role }}</span>
      <span class="text-[10px] leading-none">▾</span>
    </button>

    <template v-if="open">
      <div class="fixed inset-0 z-40" @click="open = false"></div>
      <div class="absolute right-0 z-50 mt-2 w-60 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-lg py-1">
        <div class="px-3 py-2 border-b border-slate-100 dark:border-slate-800">
          <div class="text-sm font-semibold truncate">{{ auth.user.displayName }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 truncate">{{ auth.user.email }} · {{ auth.user.role }}</div>
        </div>
        <RouterLink to="/account" @click="open = false"
                    class="block px-3 py-2 text-sm text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800">
          ⚙️ Account settings
        </RouterLink>
        <div class="px-3 py-2">
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mb-1">Theme</div>
          <div class="flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="[key, icon, label] in THEMES" :key="key" @click="setTheme(key)" :title="label"
                    class="flex-1 px-2 py-1" :class="theme === key ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ icon }}
            </button>
          </div>
        </div>
        <div class="border-t border-slate-100 dark:border-slate-800 my-1"></div>
        <button @click="open = false; emit('logout')"
                class="w-full text-left px-3 py-2 text-sm text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800">
          Sign out
        </button>
      </div>
    </template>
  </div>
</template>
