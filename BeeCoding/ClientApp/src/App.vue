<script setup>
import { ref, watch, computed, onMounted, onBeforeUnmount } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useAuth } from './stores/auth';
import { useProgress } from './stores/progress';
import ThemeToggle from './components/ThemeToggle.vue';
import AppFooter from './components/AppFooter.vue';
import UndoToast from './components/UndoToast.vue';
import ConfirmDialog from './components/ConfirmDialog.vue';
import { celebrate } from './lib/confetti';

const auth = useAuth();
const progress = useProgress();
const router = useRouter();
const route = useRoute();

const mobileNavOpen = ref(false);
watch(() => route.path, () => { mobileNavOpen.value = false; });

// The footer flows at the end of the page content (not pinned). Skip it on the
// full-height editor views where there is no natural page bottom. Problem/practice
// ids are slugs now, not numbers — match any slug but exclude the "new" segment,
// which is the ordinary (footer-having) create-problem form.
const showFooter = computed(() =>
  !/^\/boards\/[^/]+\/problems\/(?!new$)[^/]+$/.test(route.path) && !/^\/practice\/[^/]+$/.test(route.path)
  && !/^\/boards\/[^/]+\/live$/.test(route.path) && route.path !== '/playground');

// keep the header XP in sync with who's logged in
watch(() => auth.user?.id, (id) => (id ? progress.refresh() : progress.reset()), { immediate: true });

// Celebrate a level-up anywhere in the app. `levelBaseline` is the level as of the
// last hydration; null while logged out — so a fresh login that loads level 5 does
// not fire, only an actual increase afterwards.
let levelBaseline = null;
watch(
  () => [progress.ready, progress.level],
  ([ready, lvl]) => {
    if (!ready) { levelBaseline = null; return; }
    if (levelBaseline == null) { levelBaseline = lvl; return; }
    if (lvl > levelBaseline) celebrate({ count: 260, duration: 3800, waves: 5, force: true });
    levelBaseline = lvl;
  },
);

async function logout() {
  await auth.logout();
  progress.reset();
  router.push('/login');
}

// On mobile the soft keyboard overlays the page without shrinking the layout
// viewport, so it hides the editor / toolbar. Track the *visual* viewport and
// expose it as --app-h; the root uses it as its height (see style.css).
function syncViewportHeight() {
  const vv = window.visualViewport;
  const h = vv ? vv.height : window.innerHeight;
  document.documentElement.style.setProperty('--app-h', h + 'px');
}
onMounted(() => {
  syncViewportHeight();
  window.visualViewport?.addEventListener('resize', syncViewportHeight);
  window.visualViewport?.addEventListener('scroll', syncViewportHeight);
  window.addEventListener('resize', syncViewportHeight);
  window.addEventListener('orientationchange', syncViewportHeight);
});
onBeforeUnmount(() => {
  window.visualViewport?.removeEventListener('resize', syncViewportHeight);
  window.visualViewport?.removeEventListener('scroll', syncViewportHeight);
  window.removeEventListener('resize', syncViewportHeight);
  window.removeEventListener('orientationchange', syncViewportHeight);
});
</script>

<template>
  <div class="h-full min-h-0 flex flex-col">
    <header v-if="auth.user" class="shrink-0 bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800">
      <div class="max-w-6xl mx-auto px-4 py-2 flex items-center gap-x-4 gap-y-2 flex-wrap">
        <button @click="mobileNavOpen = !mobileNavOpen"
                class="order-1 md:hidden shrink-0 p-1 -ml-1 text-slate-500 dark:text-slate-400" aria-label="Menu">
          <svg v-if="!mobileNavOpen" viewBox="0 0 24 24" class="w-5 h-5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M4 6h16M4 12h16M4 18h16" />
          </svg>
          <svg v-else viewBox="0 0 24 24" class="w-5 h-5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M6 6l12 12M18 6L6 18" />
          </svg>
        </button>

        <RouterLink to="/boards" class="order-2 font-bold text-lg text-amber-600 dark:text-amber-400 shrink-0">🐝 BeeCoding</RouterLink>

        <!-- primary nav: burger-toggled dropdown on mobile, inline row after the logo on ≥md -->
        <nav :class="mobileNavOpen ? 'flex' : 'hidden'"
             class="md:flex order-5 md:order-2 w-full md:w-auto flex-col md:flex-row items-start md:items-center gap-3 md:gap-4 text-sm
                    text-slate-500 dark:text-slate-400 [&_a:hover]:text-slate-900 dark:[&_a:hover]:text-slate-100
                    border-t md:border-0 border-slate-200 dark:border-slate-800 pt-3 md:pt-0 mt-1 md:mt-0">
          <RouterLink to="/boards" @click="mobileNavOpen = false">Boards</RouterLink>
          <RouterLink to="/practice" @click="mobileNavOpen = false">Practice</RouterLink>
          <RouterLink to="/playground" @click="mobileNavOpen = false">Playground</RouterLink>
          <RouterLink to="/leaderboard" @click="mobileNavOpen = false">Leaderboard</RouterLink>
          <RouterLink v-if="auth.isTeacher" to="/bank" @click="mobileNavOpen = false">Problem bank</RouterLink>
          <RouterLink v-if="auth.user?.hasOrgAdmin" to="/org-admin" class="text-violet-600 dark:text-violet-400" @click="mobileNavOpen = false">Organization</RouterLink>
          <RouterLink v-if="auth.user?.isAdmin" to="/admin" class="text-rose-600 dark:text-rose-400" @click="mobileNavOpen = false">Admin</RouterLink>
        </nav>

        <div class="order-3 ml-auto flex items-center gap-2 sm:gap-3 text-sm shrink-0">
          <RouterLink to="/leaderboard" v-if="progress.ready"
                      class="hidden md:flex items-center gap-2" title="Your XP">
            <span class="text-xs font-semibold text-amber-600 dark:text-amber-400">Lv {{ progress.level }}</span>
            <span class="w-16 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
              <span class="block h-full bg-amber-400" :style="{ width: (progress.pct * 100) + '%' }"></span>
            </span>
            <span class="text-xs text-slate-400 dark:text-slate-500">{{ progress.xp }} XP</span>
          </RouterLink>
          <RouterLink to="/account" title="Account settings"
                      class="flex items-center gap-2 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100">
            <span class="hidden sm:block truncate max-w-[9rem]">{{ auth.user.displayName }}</span>
            <span class="px-2 py-0.5 rounded-full text-xs shrink-0"
                  :class="auth.isTeacher
                    ? 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
                    : 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300'">
              {{ auth.user.role }}
            </span>
          </RouterLink>
          <ThemeToggle />
          <button @click="logout" class="text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100">
            Sign out
          </button>
        </div>
      </div>
    </header>
    <main class="flex-1 min-h-0 flex flex-col">
      <div class="flex-1 min-h-0 overflow-y-auto flex flex-col">
        <div class="flex-1" :class="{ 'min-h-0': !showFooter }">
          <RouterView />
        </div>
        <AppFooter v-if="showFooter" class="shrink-0" />
      </div>
    </main>
    <UndoToast />
    <ConfirmDialog />
  </div>
</template>
