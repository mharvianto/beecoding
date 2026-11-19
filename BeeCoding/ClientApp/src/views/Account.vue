<script setup>
import { tr } from '../lib/i18n';
import Icon from '../components/Icon.vue';
import { hasData } from '../lib/hasData';
import { ref, computed, watch, onMounted, nextTick } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useProgress } from '../stores/progress';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';
import MfaSettings from '../components/MfaSettings.vue';
import ConnectedAccounts from '../components/ConnectedAccounts.vue';
import PasskeySettings from '../components/PasskeySettings.vue';
import OrganizationSettings from '../components/OrganizationSettings.vue';
import SnippetSettings from '../components/SnippetSettings.vue';
import BrowserNotificationSettings from '../components/BrowserNotificationSettings.vue';
import NotificationKindSettings from '../components/NotificationKindSettings.vue';
import { localDayKey } from '../lib/localDay';

const auth = useAuth();
const progress = useProgress();
const router = useRouter();
const route = useRoute();

// AI usage (only shown if the tutor is enabled on this instance)
const aiUsage = ref(null);
onMounted(async () => {
  try {
    if ((await api.get('/api/ai/enabled'))?.enabled) aiUsage.value = await api.get('/api/ai/usage');
  } catch { /* ignore */ }
});
const fmt = (n) => (n ?? 0).toLocaleString();

// ---- personal dashboard: progress across every board + practice/bank ----
const dashboard = ref(null);
const topicStats = ref(null);
const dashErr = ref('');
const ENGAGEMENT_GRANULARITY_KEY = 'beecoding.account.engagementGranularity';
const engagementGranularity = ref(localStorage.getItem(ENGAGEMENT_GRANULARITY_KEY) || 'week');   // 'hour' | 'day' | 'week'
const engagementStats = ref(null);

async function loadDashboard() {
  dashErr.value = '';
  try {
    const [d, topics] = await Promise.all([
      api.get(`/api/me/dashboard?localDay=${localDayKey()}`),
      api.get('/api/me/dashboard/topics?take=8'),
    ]);
    dashboard.value = d;
    topicStats.value = topics;
    await loadEngagement();
  } catch (e) { dashErr.value = e.message; }
}

function periodsFor(granularity) {
  return granularity === 'hour' ? 24 : granularity === 'day' ? 30 : 12;
}
async function loadEngagement() {
  dashErr.value = '';
  try {
    engagementStats.value = await api.get(
      `/api/me/dashboard/engagement?granularity=${engagementGranularity.value}&periods=${periodsFor(engagementGranularity.value)}`);
  } catch (e) { dashErr.value = e.message; }
}
function setEngagementGranularity(g) {
  engagementGranularity.value = g;
  try { localStorage.setItem(ENGAGEMENT_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadEngagement();
}

const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const shortHour = (s) => new Date(s).toLocaleTimeString(undefined, { hour: 'numeric' });
const engagementLabel = (s) => (engagementGranularity.value === 'hour' ? shortHour(s) : shortDate(s));
const attemptPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.attempts }));
const solvedPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.solved }));
const topicBarItems = () => (topicStats.value || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));

// ---- sections: /account, /account/profile, /account/security, /account/danger ----
const sections = [
  { key: 'overview', label: 'Overview', icon: 'chart', to: '/account' },
  { key: 'profile', label: 'Profile', icon: 'user', to: '/account/profile' },
  { key: 'editor', label: 'Editor', icon: 'keyboard', to: '/account/editor' },
  { key: 'security', label: 'Security', icon: 'lock', to: '/account/security' },
  { key: 'danger', label: 'Delete account', icon: 'alert', to: '/account/danger' },
];
const section = computed(() => sections.find((s) => s.key === route.params.section)?.key || 'overview');

// A link like /account/security#two-step: scroll to that block once the section has rendered.
async function scrollToHash() {
  if (!route.hash) return;
  await nextTick();
  requestAnimationFrame(() => document.getElementById(route.hash.slice(1))?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
}

// The progress dashboard is the heavy part: load it only when Overview is actually shown, and only once.
let dashboardLoaded = false;
watch(section, (s) => {
  if (s === 'overview' && !dashboardLoaded) { dashboardLoaded = true; loadDashboard(); }
  scrollToHash();
}, { immediate: true });
onMounted(() => { progress.refresh(); });

// display name
const name = ref(auth.user?.displayName || '');
const nameMsg = ref('');
const nameErr = ref('');
const nameBusy = ref(false);

async function saveName() {
  nameMsg.value = ''; nameErr.value = '';
  const v = name.value.trim();
  if (v.length < 2) { nameErr.value = tr('Display name must be at least 2 characters.'); return; }
  if (v === auth.user?.displayName) { nameMsg.value = 'No change.'; return; }
  nameBusy.value = true;
  try {
    await auth.updateDisplayName(v);
    name.value = auth.user.displayName;
    nameMsg.value = tr('Display name updated.');
  } catch (e) { nameErr.value = e.message; }
  finally { nameBusy.value = false; }
}

// change password
const cur = ref('');
const next = ref('');
const next2 = ref('');
const pwMsg = ref('');
const pwErr = ref('');
const pwBusy = ref(false);

async function changePassword() {
  pwMsg.value = ''; pwErr.value = '';
  if (next.value.length < 8) { pwErr.value = tr('New password must be at least 8 characters.'); return; }
  if (next.value !== next2.value) { pwErr.value = tr('New passwords do not match.'); return; }
  pwBusy.value = true;
  try {
    await auth.changePassword(cur.value, next.value);
    cur.value = next.value = next2.value = '';
    pwMsg.value = 'Password updated.';
  } catch (e) { pwErr.value = e.message; }
  finally { pwBusy.value = false; }
}

// first password for an account created through Google
async function setPassword() {
  pwMsg.value = ''; pwErr.value = '';
  if (next.value.length < 8) { pwErr.value = tr('Password must be at least 8 characters.'); return; }
  if (next.value !== next2.value) { pwErr.value = tr('Passwords do not match.'); return; }
  pwBusy.value = true;
  try {
    await api.post('/api/auth/set-password', { newPassword: next.value });
    await auth.fetchMe();
    next.value = next2.value = '';
    pwMsg.value = tr('Password set. You can now also sign in with your email and password.');
  } catch (e) { pwErr.value = e.message; }
  finally { pwBusy.value = false; }
}

// delete account
const showDelete = ref(false);
const delPw = ref('');
const delErr = ref('');
const delBusy = ref(false);
const ownedBoards = ref(null);   // set when the server asks for confirmation

async function deleteAccount(force = false) {
  delErr.value = ''; delBusy.value = true;
  try {
    await auth.deleteAccount(delPw.value, force);
    progress.reset();
    router.push('/register');
  } catch (e) {
    if (e.status === 409 && e.boards) { ownedBoards.value = e.boards; }
    else { delErr.value = e.message; }
  } finally { delBusy.value = false; }
}
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-10 space-y-8">
    <div>
      <h1 class="text-xl font-bold mb-1">{{ tr('Account') }}</h1>
      <p class="text-sm text-slate-500 dark:text-slate-400">
        {{ auth.user?.displayName }} · {{ auth.user?.email }} · {{ tr(auth.user?.role) }}
        <span v-if="auth.config.emailVerification" class="ml-1"
              :class="auth.user?.emailVerified ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-700 dark:text-amber-400'">
          {{ auth.user?.emailVerified ? tr('✓ email verified') : tr('⚠ email not verified') }}
        </span>
      </p>
    </div>

    <div class="grid md:grid-cols-[13rem_minmax(0,1fr)] gap-x-10 gap-y-6 items-start">
      <!-- section menu: a sticky side column on desktop, scrollable tabs on mobile -->
      <nav :aria-label="tr('Account sections')"
           class="flex flex-wrap md:flex-col md:flex-nowrap gap-1 md:sticky md:top-4 -mx-4 px-4 md:mx-0 md:px-0 pb-1 md:pb-0 border-b md:border-b-0 border-slate-200 dark:border-slate-800">
        <RouterLink v-for="item in sections" :key="item.key" :to="item.to"
                    class="flex items-center gap-2 rounded-lg px-3 py-2 text-sm whitespace-nowrap transition-colors"
                    :class="section === item.key
                      ? (item.key === 'danger' ? 'bg-red-50 text-red-700 dark:bg-red-500/10 dark:text-red-300 font-medium' : 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-300 font-medium')
                      : (item.key === 'danger' ? 'text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-500/10' : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800')">
          <Icon :name="item.icon" />
          <span>{{ item.label }}</span>
          <span v-if="item.key === 'security' && auth.user?.mfaEnabled === false" class="w-1.5 h-1.5 rounded-full bg-amber-500" :title="tr('Two-step verification is off')"></span>
        </RouterLink>
      </nav>

      <div class="min-w-0">
        <!-- Overview: progress across every board + practice/bank -->
        <div v-if="section === 'overview'" class="space-y-10">
        <!-- personal dashboard: progress across every board + practice/bank -->
        <section class="space-y-5">
          <div class="flex items-center gap-2">
            <h2 class="font-semibold text-sm">{{ tr('Progress') }}</h2>
            <button @click="loadDashboard" class="text-xs text-slate-500 dark:text-slate-400 ml-auto">{{ tr('↻ refresh') }}</button>
          </div>
          <p v-if="dashErr" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(dashErr) }}</p>

          <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
              <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Level') }}</div>
              <div class="text-2xl font-bold">Lv {{ dashboard.level }}</div>
              <div class="mt-1.5 w-full h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
                <span class="block h-full bg-amber-400" :style="{ width: (progress.pct * 100) + '%' }"></span>
              </div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{{ tr('{xp} XP · {next} to Lv {lv}', { xp: fmt(dashboard.xp), next: fmt(progress.toNext), lv: dashboard.level + 1 }) }}</div>
            </div>

            <RouterLink to="/leaderboard" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
              <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Leaderboard rank') }}</div>
              <div class="text-2xl font-bold">{{ dashboard.rank > 0 ? `#${fmt(dashboard.rank)}` : '—' }}</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{{ tr('of {n} ranked', { n: fmt(dashboard.rankedUsers) }) }}</div>
            </RouterLink>

            <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
              <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Problems solved') }}</div>
              <div class="text-2xl font-bold">{{ fmt(dashboard.solvedCount) }}</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{{ tr('{n} board(s) joined', { n: fmt(dashboard.boardsJoined) }) }}</div>
            </div>

            <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
              <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Submissions') }}</div>
              <div class="text-2xl font-bold">{{ fmt(dashboard.totalAttempts) }}</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                {{ tr('{n}% accepted', { n: dashboard.totalAttempts ? Math.round(100 * dashboard.acceptedAttempts / dashboard.totalAttempts) : 0 }) }}
              </div>
            </div>

            <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
              <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Practice streak') }}</div>
              <div class="text-2xl font-bold">{{ dashboard.streak > 0 ? `🔥 ${fmt(dashboard.streak)}` : '—' }}</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                {{ dashboard.streak > 0 ? tr('day(s) in a row') : tr('solve a practice problem to start one') }}
              </div>
              <div v-if="dashboard.longestStreak > 0" class="text-[11px] text-slate-500 dark:text-slate-400">
                {{ tr('🏆 longest: {n} day(s)', { n: fmt(dashboard.longestStreak) }) }}
              </div>
            </div>

            <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
              <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Best day') }}</div>
              <div class="text-2xl font-bold">{{ dashboard.maxSolvedInADay > 0 ? fmt(dashboard.maxSolvedInADay) : '—' }}</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{{ tr('solved in a single day') }}</div>
            </div>
          </div>
          <p v-else-if="!dashErr" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('Loading…') }}</p>

          <div>
            <div class="flex items-center gap-2 mb-2">
              <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
                <button v-for="g in [['hour', 'Hourly'], ['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                        @click="setEngagementGranularity(g[0])" class="px-2.5 py-1"
                        :class="engagementGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
                  {{ g[1] }}
                </button>
              </span>
            </div>
            <div v-if="hasData(engagementStats)" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <MiniLineChart :title="`Submissions / ${engagementGranularity}`" :points="attemptPoints()" />
              <MiniLineChart :title="`Problems solved / ${engagementGranularity}`" :points="solvedPoints()" />
            </div>
            <p v-else-if="engagementStats" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No activity in this window yet.') }}</p>
          </div>

          <div v-if="topicStats?.length">
            <h3 class="font-semibold text-sm mb-1.5">{{ tr('Topics you\'re struggling with') }}</h3>
            <p class="text-xs text-slate-500 dark:text-slate-400 mb-2">{{ tr('Lowest accept rate first, across boards and practice.') }}</p>
            <TopicBarChart :items="topicBarItems()" />
          </div>
        </section>

        <!-- AI usage -->
        <section v-if="aiUsage" class="space-y-2">
          <h2 class="font-semibold text-sm">{{ tr('AI tutor usage') }}</h2>

          <div v-if="aiUsage.blocked" class="text-xs bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-300 rounded-lg px-3 py-2">
            <Icon name="ban" /> {{ aiUsage.blockedReason || tr('AI access is currently unavailable.') }}
          </div>
          <div v-else-if="aiUsage.dailyQuota > 0">
            <div class="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400 mb-1">
              <span>{{ tr('Daily quota') }}</span>
              <span>{{ tr('{a} / {b} requests used today', { a: fmt(aiUsage.today.calls), b: fmt(aiUsage.dailyQuota) }) }}</span>
            </div>
            <div class="w-full h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
              <span class="block h-full bg-amber-400"
                    :style="{ width: Math.min(100, (aiUsage.today.calls / aiUsage.dailyQuota) * 100) + '%' }"></span>
            </div>
          </div>

          <div class="overflow-x-auto">
          <table class="w-full text-sm min-w-[360px]">
            <thead>
              <tr class="text-xs text-slate-500 dark:text-slate-400 text-left">
                <th class="font-normal py-1"></th><th class="font-normal">{{ tr('Calls') }}</th>
                <th class="font-normal">{{ tr('Prompt') }}</th><th class="font-normal">{{ tr('Reply') }}</th><th class="font-normal">{{ tr('Total tokens') }}</th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1 [&_td:not(:first-child)]:tabular-nums">
              <tr><td class="text-slate-500 dark:text-slate-400">{{ tr('Today') }}</td>
                <td>{{ fmt(aiUsage.today.calls) }}</td><td>{{ fmt(aiUsage.today.promptTokens) }}</td>
                <td>{{ fmt(aiUsage.today.completionTokens) }}</td><td class="font-medium">{{ fmt(aiUsage.today.totalTokens) }}</td></tr>
              <tr><td class="text-slate-500 dark:text-slate-400">{{ tr('This month') }}</td>
                <td>{{ fmt(aiUsage.month.calls) }}</td><td>{{ fmt(aiUsage.month.promptTokens) }}</td>
                <td>{{ fmt(aiUsage.month.completionTokens) }}</td><td class="font-medium">{{ fmt(aiUsage.month.totalTokens) }}</td></tr>
              <tr><td class="text-slate-500 dark:text-slate-400">{{ tr('All time') }}</td>
                <td>{{ fmt(aiUsage.allTime.calls) }}</td><td>{{ fmt(aiUsage.allTime.promptTokens) }}</td>
                <td>{{ fmt(aiUsage.allTime.completionTokens) }}</td><td class="font-medium">{{ fmt(aiUsage.allTime.totalTokens) }}</td></tr>
            </tbody>
          </table>
          </div>
        </section>
        </div>

        <!-- Profile -->
        <div v-else-if="section === 'profile'" class="max-w-xl space-y-10">
        <OrganizationSettings />
        <!-- display name -->
        <section class="space-y-3">
          <h2 class="font-semibold text-sm">{{ tr('Display name') }}</h2>
          <p class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Shown on the board, wall cards and leaderboard.') }}</p>
          <div class="flex gap-2">
            <input v-model="name" maxlength="40" :placeholder="tr('Your name')" :aria-label="tr('Your name')" @keyup.enter="saveName"
                   class="flex-1 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
            <button @click="saveName" :disabled="nameBusy || !name.trim() || name.trim() === auth.user?.displayName"
                    class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
              {{ nameBusy ? '…' : tr('Save') }}
            </button>
          </div>
          <p v-if="nameErr" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(nameErr) }}</p>
          <p v-if="nameMsg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ tr(nameMsg) }}</p>
        </section>
        <NotificationKindSettings />
        <BrowserNotificationSettings />
        </div>

        <!-- Editor -->
        <div v-else-if="section === 'editor'" class="max-w-2xl space-y-10">
        <SnippetSettings />
        </div>

        <!-- Security -->
        <div v-else-if="section === 'security'" class="max-w-xl space-y-10">
        <!-- change password -->
        <section v-if="auth.user?.hasPassword === false" class="space-y-3">
          <h2 class="font-semibold text-sm">{{ tr('Set a password') }}</h2>
          <p class="text-sm text-slate-500 dark:text-slate-400">
            {{ tr('You signed up with Google, so this account has no password yet. Set one to sign in with your email too, and to confirm sensitive changes.') }}
          </p>
          <div class="space-y-3">
            <input v-model="next" type="password" :placeholder="tr('New password (min 8 chars)')" :aria-label="tr('New password (min 8 chars)')" autocomplete="new-password"
                   class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
            <input v-model="next2" type="password" :placeholder="tr('Repeat password')" :aria-label="tr('Repeat password')" autocomplete="new-password" @keyup.enter="setPassword"
                   class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
            <p v-if="pwErr" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(pwErr) }}</p>
            <p v-if="pwMsg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ tr(pwMsg) }}</p>
            <button @click="setPassword" :disabled="pwBusy || !next"
                    class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
              {{ pwBusy ? '…' : tr('Set password') }}
            </button>
          </div>
        </section>
        <section v-else class="space-y-3">
          <h2 class="font-semibold text-sm">{{ tr('Change password') }}</h2>
          <div class="space-y-3">
            <input v-model="cur" type="password" :placeholder="tr('Current password')" :aria-label="tr('Current password')" autocomplete="current-password"
                   class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
            <input v-model="next" type="password" :placeholder="tr('New password (min 8 chars)')" :aria-label="tr('New password (min 8 chars)')" autocomplete="new-password"
                   class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
            <input v-model="next2" type="password" :placeholder="tr('Repeat new password')" :aria-label="tr('Repeat new password')" autocomplete="new-password"
                   @keyup.enter="changePassword"
                   class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
            <p v-if="pwErr" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(pwErr) }}</p>
            <p v-if="pwMsg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ tr(pwMsg) }}</p>
            <button @click="changePassword" :disabled="pwBusy || !cur || !next"
                    class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
              {{ pwBusy ? '…' : tr('Update password') }}
            </button>
          </div>
        </section>

        <ConnectedAccounts />

        <PasskeySettings />

        <MfaSettings />
        </div>

        <!-- Danger zone -->
        <div v-else class="max-w-xl space-y-10">
        <!-- delete account -->
        <section class="space-y-3 border border-red-200 dark:border-red-500/30 rounded-xl p-4">
          <h2 class="font-semibold text-sm text-red-600 dark:text-red-400">{{ tr('Delete account') }}</h2>
          <p class="text-sm text-slate-500 dark:text-slate-400">
            {{ tr('Permanently removes your account, your submissions, and your practice progress.') }}
            <span v-if="auth.isTeacher">{{ tr('Boards you own — and everyone\'s work on them — go too.') }}</span>
            {{ tr('This cannot be undone.') }}
          </p>

          <button v-if="!showDelete" @click="showDelete = true"
                  class="border border-red-300 dark:border-red-500/40 text-red-600 dark:text-red-400 rounded-lg px-4 py-2 text-sm font-medium">
            {{ tr('Delete my account…') }}
          </button>

          <template v-else>
            <input v-model="delPw" type="password" :placeholder="tr('Confirm with your password')" :aria-label="tr('Confirm with your password')" autocomplete="current-password"
                   class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />

            <div v-if="ownedBoards" class="text-sm bg-red-50 dark:bg-red-500/10 rounded-lg p-3 space-y-1">
              <p class="text-red-700 dark:text-red-300 font-medium">{{ tr('These boards will be deleted with all their submissions:') }}</p>
              <ul class="list-disc pl-5 text-slate-600 dark:text-slate-300">
                <li v-for="b in ownedBoards" :key="b.slug">{{ b.title }}</li>
              </ul>
            </div>

            <p v-if="delErr" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ tr(delErr) }}</p>

            <div class="flex gap-2">
              <button @click="deleteAccount(!!ownedBoards)" :disabled="delBusy || !delPw"
                      class="bg-red-600 hover:bg-red-700 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
                {{ delBusy ? '…' : ownedBoards ? tr('Yes, delete everything') : tr('Delete account') }}
              </button>
              <button @click="showDelete = false; ownedBoards = null; delPw = ''; delErr = ''"
                      class="text-slate-500 dark:text-slate-400 rounded-lg px-4 py-2 text-sm">
                {{ tr('Cancel') }}
              </button>
            </div>
          </template>
        </section>
        </div>
      </div>
    </div>
  </div>
</template>
