<script setup>
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';

const props = defineProps({ slug: { type: String, required: true } });
const router = useRouter();

const board = ref(null);
const error = ref('');
const stats = ref(null);

const ENGAGEMENT_GRANULARITY_KEY = 'beecoding.board.stats.engagementGranularity';
const AI_GRANULARITY_KEY = 'beecoding.board.stats.aiGranularity';
const engagementGranularity = ref(localStorage.getItem(ENGAGEMENT_GRANULARITY_KEY) || 'week');   // 'hour' | 'day' | 'week'
const engagementStats = ref(null);
const aiGranularity = ref(localStorage.getItem(AI_GRANULARITY_KEY) || 'week');                    // 'day' | 'week'
const aiEngagementStats = ref(null);

function periodsFor(granularity) {
  return granularity === 'hour' ? 48 : granularity === 'day' ? 14 : 12;
}
async function loadEngagement() {
  try {
    engagementStats.value = await api.get(
      `/api/boards/${props.slug}/stats/engagement?granularity=${engagementGranularity.value}&periods=${periodsFor(engagementGranularity.value)}`);
  } catch (e) { error.value = e.message; }
}
function setEngagementGranularity(g) {
  engagementGranularity.value = g;
  try { localStorage.setItem(ENGAGEMENT_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadEngagement();
}
async function loadAiEngagement() {
  try {
    aiEngagementStats.value = await api.get(
      `/api/boards/${props.slug}/stats/ai-engagement?granularity=${aiGranularity.value}&periods=${periodsFor(aiGranularity.value)}`);
  } catch (e) { error.value = e.message; }
}
function setAiGranularity(g) {
  aiGranularity.value = g;
  try { localStorage.setItem(AI_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadAiEngagement();
}
const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const shortHour = (s) => new Date(s).toLocaleTimeString(undefined, { hour: 'numeric' });
const engagementLabel = (s) => (engagementGranularity.value === 'hour' ? shortHour(s) : shortDate(s));
const activeUserPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.activeUsers }));
const submissionPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.submissions }));
const aiCallPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.calls }));
const aiTokenPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.totalTokens }));
const topicBarItems = () => (stats.value?.topics || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));

onMounted(async () => {
  try {
    board.value = await api.get(`/api/boards/${props.slug}`);
    if (board.value.role === 'Student') { router.replace(`/boards/${props.slug}`); return; }
    stats.value = await api.get(`/api/boards/${props.slug}/stats`);
    await Promise.all([loadEngagement(), loadAiEngagement()]);
  } catch (e) { error.value = e.message; }
});
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-6" v-if="board">
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to {{ board.title }}</RouterLink>
    <div class="flex items-center justify-between mt-2 mb-4">
      <h1 class="text-xl font-bold">Statistics</h1>
      <span class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
        <RouterLink :to="`/boards/${slug}/stats`" class="px-3 py-1.5 bg-slate-800 text-white dark:bg-slate-600">Statistics</RouterLink>
        <RouterLink :to="`/boards/${slug}/submissions`" class="px-3 py-1.5 text-slate-500 dark:text-slate-400">Submissions</RouterLink>
      </span>
    </div>
    <p v-if="error" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ error }}</p>

    <div class="space-y-4">
      <div v-if="stats" class="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="text-xs text-slate-400 dark:text-slate-500">Students</div>
          <div class="text-xl font-bold">{{ stats.totalStudents }}</div>
        </div>
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="text-xs text-slate-400 dark:text-slate-500">Problems</div>
          <div class="text-xl font-bold">{{ stats.totalProblems }}</div>
        </div>
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3 col-span-2 sm:col-span-1">
          <div class="text-xs text-slate-400 dark:text-slate-500">Submissions</div>
          <div class="text-xl font-bold">{{ stats.totalSubmissions }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500">
            {{ stats.totalSubmissions ? Math.round(100 * stats.acceptedSubmissions / stats.totalSubmissions) : 0 }}% accepted
          </div>
        </div>
      </div>
      <p v-else class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">Engagement</h2>
          <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="g in [['hour', 'Hourly'], ['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                    @click="setEngagementGranularity(g[0])" class="px-2.5 py-1"
                    :class="engagementGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ g[1] }}
            </button>
          </span>
        </div>
        <div v-if="engagementStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="`Active students / ${engagementGranularity}`" :points="activeUserPoints()" />
          <MiniLineChart :title="`Submissions / ${engagementGranularity}`" :points="submissionPoints()" />
        </div>
        <p v-else-if="engagementStats" class="text-slate-400 dark:text-slate-500 text-sm">No activity in this window yet.</p>
      </div>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">AI usage</h2>
          <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="g in [['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                    @click="setAiGranularity(g[0])" class="px-2.5 py-1"
                    :class="aiGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ g[1] }}
            </button>
          </span>
        </div>
        <div v-if="aiEngagementStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="`AI calls / ${aiGranularity}`" :points="aiCallPoints()" />
          <MiniLineChart :title="`AI tokens / ${aiGranularity}`" :points="aiTokenPoints()" />
        </div>
        <p v-else-if="aiEngagementStats" class="text-slate-400 dark:text-slate-500 text-sm">No AI usage in this window yet.</p>
      </div>

      <div v-if="stats?.topics?.length">
        <h2 class="font-semibold text-sm mb-1.5">Top topics by attempts</h2>
        <TopicBarChart :items="topicBarItems()" />
      </div>
    </div>
  </div>
</template>
