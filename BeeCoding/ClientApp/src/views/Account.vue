<script setup>
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useProgress } from '../stores/progress';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';

const auth = useAuth();
const progress = useProgress();
const router = useRouter();

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
const engagementGranularity = ref('week');   // 'hour' | 'day' | 'week'
const engagementStats = ref(null);

async function loadDashboard() {
  dashErr.value = '';
  try {
    const [d, topics] = await Promise.all([
      api.get('/api/me/dashboard'),
      api.get('/api/me/dashboard/topics?take=8'),
    ]);
    dashboard.value = d;
    topicStats.value = topics;
    await loadEngagement();
  } catch (e) { dashErr.value = e.message; }
}

function periodsFor(granularity) {
  return granularity === 'hour' ? 48 : granularity === 'day' ? 14 : 12;
}
async function loadEngagement() {
  dashErr.value = '';
  try {
    engagementStats.value = await api.get(
      `/api/me/dashboard/engagement?granularity=${engagementGranularity.value}&periods=${periodsFor(engagementGranularity.value)}`);
  } catch (e) { dashErr.value = e.message; }
}
function setEngagementGranularity(g) { engagementGranularity.value = g; loadEngagement(); }

const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const shortHour = (s) => new Date(s).toLocaleTimeString(undefined, { hour: 'numeric' });
const engagementLabel = (s) => (engagementGranularity.value === 'hour' ? shortHour(s) : shortDate(s));
const attemptPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.attempts }));
const solvedPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.solved }));
const topicBarItems = () => (topicStats.value || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));

onMounted(() => { progress.refresh(); loadDashboard(); });

// display name
const name = ref(auth.user?.displayName || '');
const nameMsg = ref('');
const nameErr = ref('');
const nameBusy = ref(false);

async function saveName() {
  nameMsg.value = ''; nameErr.value = '';
  const v = name.value.trim();
  if (v.length < 2) { nameErr.value = 'Display name must be at least 2 characters.'; return; }
  if (v === auth.user?.displayName) { nameMsg.value = 'No change.'; return; }
  nameBusy.value = true;
  try {
    await auth.updateDisplayName(v);
    name.value = auth.user.displayName;
    nameMsg.value = 'Display name updated.';
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
  if (next.value.length < 8) { pwErr.value = 'New password must be at least 8 characters.'; return; }
  if (next.value !== next2.value) { pwErr.value = 'New passwords do not match.'; return; }
  pwBusy.value = true;
  try {
    await auth.changePassword(cur.value, next.value);
    cur.value = next.value = next2.value = '';
    pwMsg.value = 'Password updated.';
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
  <div class="max-w-6xl mx-auto px-4 py-10 space-y-10">
    <div>
      <h1 class="text-xl font-bold mb-1">Account</h1>
      <p class="text-sm text-slate-500 dark:text-slate-400">
        {{ auth.user?.displayName }} · {{ auth.user?.email }} · {{ auth.user?.role }}
      </p>
    </div>

    <!-- personal dashboard: progress across every board + practice/bank -->
    <section class="space-y-5">
      <div class="flex items-center gap-2">
        <h2 class="font-semibold text-sm">Progress</h2>
        <button @click="loadDashboard" class="text-xs text-slate-500 dark:text-slate-400 ml-auto">↻ refresh</button>
      </div>
      <p v-if="dashErr" class="text-sm text-red-600 dark:text-red-400">{{ dashErr }}</p>

      <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-400 dark:text-slate-500">Level</div>
          <div class="text-2xl font-bold">Lv {{ dashboard.level }}</div>
          <div class="mt-1.5 w-full h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
            <span class="block h-full bg-amber-400" :style="{ width: (progress.pct * 100) + '%' }"></span>
          </div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">{{ fmt(dashboard.xp) }} XP</div>
        </div>

        <RouterLink to="/leaderboard" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-400 dark:text-slate-500">Leaderboard rank</div>
          <div class="text-2xl font-bold">{{ dashboard.rank > 0 ? `#${fmt(dashboard.rank)}` : '—' }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">of {{ fmt(dashboard.rankedUsers) }} ranked</div>
        </RouterLink>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-400 dark:text-slate-500">Problems solved</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.solvedCount) }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">{{ fmt(dashboard.boardsJoined) }} board(s) joined</div>
        </div>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-400 dark:text-slate-500">Submissions</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalAttempts) }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
            {{ dashboard.totalAttempts ? Math.round(100 * dashboard.acceptedAttempts / dashboard.totalAttempts) : 0 }}% accepted
          </div>
        </div>
      </div>
      <p v-else-if="!dashErr" class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>

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
        <div v-if="engagementStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="`Submissions / ${engagementGranularity}`" :points="attemptPoints()" />
          <MiniLineChart :title="`Problems solved / ${engagementGranularity}`" :points="solvedPoints()" />
        </div>
        <p v-else-if="engagementStats" class="text-slate-400 dark:text-slate-500 text-sm">No activity in this window yet.</p>
      </div>

      <div v-if="topicStats?.length">
        <h3 class="font-semibold text-sm mb-1.5">Topics you're struggling with</h3>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">Lowest accept rate first, across boards and practice.</p>
        <TopicBarChart :items="topicBarItems()" />
      </div>
    </section>

    <div class="space-y-10">
    <!-- display name -->
    <section class="space-y-3">
      <h2 class="font-semibold text-sm">Display name</h2>
      <p class="text-xs text-slate-400 dark:text-slate-500">Shown on the board, wall cards and leaderboard.</p>
      <div class="flex gap-2 max-w-sm">
        <input v-model="name" maxlength="40" placeholder="Your name" @keyup.enter="saveName"
               class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="saveName" :disabled="nameBusy || !name.trim() || name.trim() === auth.user?.displayName"
                class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
          {{ nameBusy ? '…' : 'Save' }}
        </button>
      </div>
      <p v-if="nameErr" class="text-sm text-red-600 dark:text-red-400">{{ nameErr }}</p>
      <p v-if="nameMsg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ nameMsg }}</p>
    </section>

    <!-- AI usage -->
    <section v-if="aiUsage" class="space-y-2">
      <h2 class="font-semibold text-sm">AI tutor usage</h2>

      <div v-if="aiUsage.blocked" class="text-xs bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-300 rounded-lg px-3 py-2">
        🚫 {{ aiUsage.blockedReason || 'AI access is currently unavailable.' }}
      </div>
      <div v-else-if="aiUsage.dailyQuota > 0">
        <div class="flex items-center justify-between text-xs text-slate-400 dark:text-slate-500 mb-1">
          <span>Daily quota</span>
          <span>{{ fmt(aiUsage.today.calls) }} / {{ fmt(aiUsage.dailyQuota) }} requests used today</span>
        </div>
        <div class="w-full h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
          <span class="block h-full bg-amber-400"
                :style="{ width: Math.min(100, (aiUsage.today.calls / aiUsage.dailyQuota) * 100) + '%' }"></span>
        </div>
      </div>

      <div class="overflow-x-auto">
      <table class="w-full text-sm min-w-[360px]">
        <thead>
          <tr class="text-xs text-slate-400 dark:text-slate-500 text-left">
            <th class="font-normal py-1"></th><th class="font-normal">Calls</th>
            <th class="font-normal">Prompt</th><th class="font-normal">Reply</th><th class="font-normal">Total tokens</th>
          </tr>
        </thead>
        <tbody class="[&_td]:py-1 [&_td:not(:first-child)]:tabular-nums">
          <tr><td class="text-slate-500 dark:text-slate-400">Today</td>
            <td>{{ fmt(aiUsage.today.calls) }}</td><td>{{ fmt(aiUsage.today.promptTokens) }}</td>
            <td>{{ fmt(aiUsage.today.completionTokens) }}</td><td class="font-medium">{{ fmt(aiUsage.today.totalTokens) }}</td></tr>
          <tr><td class="text-slate-500 dark:text-slate-400">This month</td>
            <td>{{ fmt(aiUsage.month.calls) }}</td><td>{{ fmt(aiUsage.month.promptTokens) }}</td>
            <td>{{ fmt(aiUsage.month.completionTokens) }}</td><td class="font-medium">{{ fmt(aiUsage.month.totalTokens) }}</td></tr>
          <tr><td class="text-slate-500 dark:text-slate-400">All time</td>
            <td>{{ fmt(aiUsage.allTime.calls) }}</td><td>{{ fmt(aiUsage.allTime.promptTokens) }}</td>
            <td>{{ fmt(aiUsage.allTime.completionTokens) }}</td><td class="font-medium">{{ fmt(aiUsage.allTime.totalTokens) }}</td></tr>
        </tbody>
      </table>
      </div>
    </section>

    <!-- change password -->
    <section class="space-y-3">
      <h2 class="font-semibold text-sm">Change password</h2>
      <div class="max-w-sm space-y-3">
        <input v-model="cur" type="password" placeholder="Current password" autocomplete="current-password"
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <input v-model="next" type="password" placeholder="New password (min 8 chars)" autocomplete="new-password"
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <input v-model="next2" type="password" placeholder="Repeat new password" autocomplete="new-password"
               @keyup.enter="changePassword"
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <p v-if="pwErr" class="text-sm text-red-600 dark:text-red-400">{{ pwErr }}</p>
        <p v-if="pwMsg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ pwMsg }}</p>
        <button @click="changePassword" :disabled="pwBusy || !cur || !next"
                class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
          {{ pwBusy ? '…' : 'Update password' }}
        </button>
      </div>
    </section>

    <!-- delete account -->
    <section class="space-y-3 max-w-md border border-red-200 dark:border-red-500/30 rounded-xl p-4">
      <h2 class="font-semibold text-sm text-red-600 dark:text-red-400">Delete account</h2>
      <p class="text-sm text-slate-500 dark:text-slate-400">
        Permanently removes your account, your submissions, and your practice progress.
        <span v-if="auth.isTeacher">Boards you own — and everyone's work on them — go too.</span>
        This cannot be undone.
      </p>

      <button v-if="!showDelete" @click="showDelete = true"
              class="border border-red-300 dark:border-red-500/40 text-red-600 dark:text-red-400 rounded-lg px-4 py-2 text-sm font-medium">
        Delete my account…
      </button>

      <template v-else>
        <input v-model="delPw" type="password" placeholder="Confirm with your password" autocomplete="current-password"
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />

        <div v-if="ownedBoards" class="text-sm bg-red-50 dark:bg-red-500/10 rounded-lg p-3 space-y-1">
          <p class="text-red-700 dark:text-red-300 font-medium">These boards will be deleted with all their submissions:</p>
          <ul class="list-disc pl-5 text-slate-600 dark:text-slate-300">
            <li v-for="b in ownedBoards" :key="b.slug">{{ b.title }}</li>
          </ul>
        </div>

        <p v-if="delErr" class="text-sm text-red-600 dark:text-red-400">{{ delErr }}</p>

        <div class="flex gap-2">
          <button @click="deleteAccount(!!ownedBoards)" :disabled="delBusy || !delPw"
                  class="bg-red-600 hover:bg-red-700 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
            {{ delBusy ? '…' : ownedBoards ? 'Yes, delete everything' : 'Delete account' }}
          </button>
          <button @click="showDelete = false; ownedBoards = null; delPw = ''; delErr = ''"
                  class="text-slate-500 dark:text-slate-400 rounded-lg px-4 py-2 text-sm">
            Cancel
          </button>
        </div>
      </template>
    </section>
    </div>
  </div>
</template>
