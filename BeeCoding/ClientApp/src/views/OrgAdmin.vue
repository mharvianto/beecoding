<script setup>
import { ref, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useConfirmDialog } from '../stores/confirmDialog';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';
import TableViewToggle from '../components/TableViewToggle.vue';
import VerdictBadge from '../components/VerdictBadge.vue';
import SubmissionView from '../components/SubmissionView.vue';
import PlagiarismTable from '../components/PlagiarismTable.vue';
import { tableView } from '../lib/tableView';

const confirmDialog = useConfirmDialog();
const route = useRoute();
const router = useRouter();

const orgs = ref(null);
const orgId = ref(null);
const tabDefs = [['dashboard', 'Dashboard'], ['members', 'Members'], ['boards', 'Boards'], ['submissions', 'Submissions'], ['plagiarism', 'Plagiarism'], ['ai', 'AI settings'], ['lti', 'LTI']];
// Deep-linkable: /org-admin/members etc. — reload/share/bookmark lands on the same tab.
const tab = ref(tabDefs.some((t) => t[0] === route.params.tab) ? route.params.tab : 'dashboard');
const mobileTabsOpen = ref(false);
const err = ref('');

const summary = ref(null);
const dashboard = ref(null);
const topicStats = ref(null);

// granularity choice persists per-browser (not per-org) — same toggle regardless of which
// org you're looking at
const ENGAGEMENT_GRANULARITY_KEY = 'beecoding.orgAdmin.engagementGranularity';
const AI_GRANULARITY_KEY = 'beecoding.orgAdmin.aiGranularity';
const engagementGranularity = ref(localStorage.getItem(ENGAGEMENT_GRANULARITY_KEY) || 'week');   // 'hour' | 'day' | 'week'
const engagementStats = ref(null);
const aiGranularity = ref(localStorage.getItem(AI_GRANULARITY_KEY) || 'week');                    // 'day' | 'week' — AiUsage has no finer data than a day
const aiEngagementStats = ref(null);
const members = ref(null);
const boards = ref(null);
const plagiarismBoardId = ref(null);
const plagiarismPairs = ref([]);
const plagiarismLoading = ref(false);
const plagiarismViewSubmission = ref(null);
const aiSettings = ref(null);
const aiSaving = ref(false);
const aiProvider = ref(null);
const aiProviderForm = ref({ apiKey: '', baseUrl: '', model: '', generateModel: '' });
const aiProviderSaving = ref(false);
const aiProviderMsg = ref('');

const newMemberEmail = ref('');
const newMemberRole = ref('Member');
const importingMembers = ref(false);
const memberImportResult = ref(null);

const bulkBoardOwnerEmail = ref('');
const bulkBoardTitles = ref('');
const bulkBoardBusy = ref(false);
const bulkBoardResult = ref(null);

const ltiPlatforms = ref(null);
const ltiToolConfig = ref(null);
const ltiEditing = ref(null);   // id of the platform being edited, or 'new'
const ltiForm = ref({});
const ltiCopyMsg = ref('');
const blankLtiForm = () => ({ name: '', issuer: '', clientId: '', deploymentIds: '', authLoginUrl: '', authTokenUrl: '', jwksUrl: '', enabled: true });

async function loadOrgs() {
  err.value = '';
  try {
    orgs.value = await api.get('/api/org-admin/mine');
    if (orgs.value.length && !orgId.value) {
      // Deep-linkable: /org-admin/<slug>/<tab> — reload/share/bookmark lands on the same
      // organization, not always back to the first one in the list.
      const wanted = orgs.value.find((o) => o.slug === route.params.slug);
      selectOrg((wanted || orgs.value[0]).id);
    }
  } catch (e) { err.value = e.message; }
}

function selectOrg(id) {
  orgId.value = id;
  summary.value = null; dashboard.value = null; engagementStats.value = null; aiEngagementStats.value = null; topicStats.value = null;
  members.value = null; boards.value = null; aiSettings.value = null; aiProvider.value = null;
  ltiPlatforms.value = null; ltiToolConfig.value = null; ltiEditing.value = null;
  const org = orgs.value?.find((o) => o.id === id);
  if (org && (route.params.slug !== org.slug || route.params.tab !== tab.value))
    router.replace(`/org-admin/${org.slug}/${tab.value}`);
  loadTab(tab.value);
}

function switchTab(id) {
  mobileTabsOpen.value = false;
  if (id === tab.value) return;
  tab.value = id;
  const org = orgs.value?.find((o) => o.id === orgId.value);
  if (org) router.replace(`/org-admin/${org.slug}/${id}`);
  loadTab(id);
}
// Browser back/forward (or a direct link to /org-admin/<slug>/<tab>) changes route.params
// without going through switchTab/selectOrg — keep local state in sync.
watch(() => route.params.tab, (t) => {
  const id = tabDefs.some(([k]) => k === t) ? t : 'dashboard';
  if (id !== tab.value) { tab.value = id; loadTab(id); }
});
watch(() => route.params.slug, (slug) => {
  const org = orgs.value?.find((o) => o.slug === slug);
  if (org && org.id !== orgId.value) selectOrg(org.id);
});
function loadTab(id) {
  if (!orgId.value) return;
  loadSummary();
  if (id === 'dashboard' && !dashboard.value) loadDashboard();
  else if (id === 'members' && !members.value) loadMembers();
  else if (id === 'boards' && !boards.value) loadBoards();
  else if (id === 'submissions' && !submissionRows.value) loadSubmissions();
  else if (id === 'plagiarism' && !boards.value) loadBoards();
  else if (id === 'ai') {
    if (!aiSettings.value) loadAiSettings();
    if (!aiProvider.value) loadAiProvider();
  } else if (id === 'lti') {
    if (!ltiPlatforms.value) loadLtiPlatforms();
    if (!ltiToolConfig.value) loadLtiToolConfig();
  }
}

async function loadSummary() {
  try { summary.value = await api.get(`/api/org-admin/${orgId.value}/summary`); } catch (e) { err.value = e.message; }
}

async function loadDashboard() {
  err.value = '';
  try {
    const [d, topics] = await Promise.all([
      api.get(`/api/org-admin/${orgId.value}/dashboard`),
      api.get(`/api/org-admin/${orgId.value}/dashboard/topics?take=8`),
    ]);
    dashboard.value = d;
    topicStats.value = topics;
    await Promise.all([loadEngagement(), loadAiEngagement()]);
  } catch (e) { err.value = e.message; }
}

function periodsFor(granularity) {
  return granularity === 'hour' ? 48 : granularity === 'day' ? 14 : 12;
}
async function loadEngagement() {
  if (!orgId.value) return;
  err.value = '';
  try {
    engagementStats.value = await api.get(
      `/api/org-admin/${orgId.value}/dashboard/engagement?granularity=${engagementGranularity.value}&periods=${periodsFor(engagementGranularity.value)}`);
  } catch (e) { err.value = e.message; }
}
function setEngagementGranularity(g) {
  engagementGranularity.value = g;
  try { localStorage.setItem(ENGAGEMENT_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadEngagement();
}

async function loadAiEngagement() {
  if (!orgId.value) return;
  err.value = '';
  try {
    aiEngagementStats.value = await api.get(
      `/api/org-admin/${orgId.value}/dashboard/ai-engagement?granularity=${aiGranularity.value}&periods=${periodsFor(aiGranularity.value)}`);
  } catch (e) { err.value = e.message; }
}
function setAiGranularity(g) {
  aiGranularity.value = g;
  try { localStorage.setItem(AI_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadAiEngagement();
}

const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const shortHour = (s) => new Date(s).toLocaleTimeString(undefined, { hour: 'numeric' });
const engagementLabel = (s) => (engagementGranularity.value === 'hour' ? shortHour(s) : shortDate(s));
const fmt = (n) => (n ?? 0).toLocaleString();
const activeUserPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.activeUsers }));
const submissionPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.submissions }));
const aiCallPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.calls }));
const aiTokenPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.totalTokens }));
const topicBarItems = () => (topicStats.value || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));
async function loadMembers() {
  err.value = '';
  try { members.value = await api.get(`/api/org-admin/${orgId.value}/members`); } catch (e) { err.value = e.message; }
}
async function addMember() {
  if (!newMemberEmail.value.trim()) return;
  err.value = '';
  try {
    await api.post(`/api/org-admin/${orgId.value}/members`, { email: newMemberEmail.value.trim(), orgRole: newMemberRole.value });
    newMemberEmail.value = '';
    await loadMembers();
  } catch (e) { err.value = e.message; }
}
async function changeMemberRole(m, role) {
  if (role === m.orgRole) return;
  err.value = '';
  try { await api.put(`/api/org-admin/${orgId.value}/members/${m.userId}`, { orgRole: role }); m.orgRole = role; }
  catch (e) { err.value = e.message; await loadMembers(); }
}
async function removeMember(m) {
  if (!(await confirmDialog.ask(`Remove "${m.displayName}" from this organization?`, { confirmLabel: 'Remove' }))) return;
  err.value = '';
  try { await api.del(`/api/org-admin/${orgId.value}/members/${m.userId}`); await loadMembers(); }
  catch (e) { err.value = e.message; }
}

async function importMembersCsv(ev) {
  const file = ev.target.files?.[0];
  ev.target.value = '';
  if (!file) return;
  err.value = ''; memberImportResult.value = null; importingMembers.value = true;
  try {
    const csv = await file.text();
    memberImportResult.value = await api.post(`/api/org-admin/${orgId.value}/members/import`, { csv });
    await loadMembers();
  } catch (e) { err.value = e.message; }
  finally { importingMembers.value = false; }
}

async function loadBoards() {
  err.value = '';
  try { boards.value = await api.get(`/api/org-admin/${orgId.value}/boards`); } catch (e) { err.value = e.message; }
}

async function loadPlagiarism() {
  if (!plagiarismBoardId.value) { plagiarismPairs.value = []; return; }
  err.value = ''; plagiarismLoading.value = true;
  try {
    plagiarismPairs.value = await api.get(`/api/org-admin/${orgId.value}/plagiarism?boardId=${plagiarismBoardId.value}`);
  } catch (e) { err.value = e.message; }
  finally { plagiarismLoading.value = false; }
}

// ---- tags: editable here even though the org admin isn't a member of these boards —
// BoardsController.Update lets an org admin set Tags (only) on any board in their org ----
const editingBoardTagsSlug = ref(null);
const boardTagsInput = ref('');
function startEditBoardTags(b) { editingBoardTagsSlug.value = b.slug; boardTagsInput.value = b.tags || ''; }
async function saveBoardTags(b) {
  try {
    const updated = await api.patch(`/api/boards/${b.slug}`, { tags: boardTagsInput.value });
    b.tags = updated.tags;
    editingBoardTagsSlug.value = null;
  } catch (e) { err.value = e.message; }
}

// ---- submissions: this org's boards, plus practice submissions by this org's members ----
const submissionRows = ref(null);
const submissionQ = ref('');
const submissionVerdict = ref('');
const submissionSource = ref('');
const submissionUserId = ref(null);      // set when jumping here from a member row
const submissionUserLabel = ref('');     // that member's name, shown as an active filter chip
const submissionsPage = ref(1);
const submissionsPageSize = ref(50);
const submissionsTotal = ref(0);
const viewSubmission = ref(null);        // { id, source, authorName } | null

async function loadSubmissions() {
  err.value = '';
  try {
    const p = new URLSearchParams({ page: String(submissionsPage.value), pageSize: String(submissionsPageSize.value) });
    if (submissionQ.value.trim()) p.set('q', submissionQ.value.trim());
    if (submissionVerdict.value) p.set('verdict', submissionVerdict.value);
    if (submissionSource.value) p.set('source', submissionSource.value);
    if (submissionUserId.value) p.set('userId', submissionUserId.value);
    const result = await api.get(`/api/org-admin/${orgId.value}/submissions?${p}`);
    submissionRows.value = result.rows;
    submissionsTotal.value = result.total;
  } catch (e) { err.value = e.message; }
}
function searchSubmissions() { submissionsPage.value = 1; loadSubmissions(); }
function submissionsPrevPage() { if (submissionsPage.value > 1) { submissionsPage.value--; loadSubmissions(); } }
function submissionsNextPage() { if (submissionsPage.value * submissionsPageSize.value < submissionsTotal.value) { submissionsPage.value++; loadSubmissions(); } }
function openSubmission(s) { viewSubmission.value = { id: s.id, source: s.source.toLowerCase(), authorName: s.userDisplayName }; }
function clearSubmissionUserFilter() { submissionUserId.value = null; submissionUserLabel.value = ''; searchSubmissions(); }
function viewMemberSubmissions(m) {
  submissionUserId.value = m.userId;
  submissionUserLabel.value = m.displayName;
  mobileTabsOpen.value = false;
  tab.value = 'submissions';
  const org = orgs.value?.find((o) => o.id === orgId.value);
  if (org) router.replace(`/org-admin/${org.slug}/submissions`);
  searchSubmissions();
}

async function bulkCreateBoards() {
  const titles = bulkBoardTitles.value.split('\n').map((t) => t.trim()).filter(Boolean);
  if (!bulkBoardOwnerEmail.value.trim() || !titles.length) return;
  err.value = ''; bulkBoardResult.value = null; bulkBoardBusy.value = true;
  try {
    bulkBoardResult.value = await api.post(`/api/org-admin/${orgId.value}/boards/bulk`, {
      ownerEmail: bulkBoardOwnerEmail.value.trim(), titles,
    });
    bulkBoardTitles.value = '';
    await loadBoards();
  } catch (e) { err.value = e.message; }
  finally { bulkBoardBusy.value = false; }
}

async function loadAiSettings() {
  err.value = '';
  try { aiSettings.value = await api.get(`/api/org-admin/${orgId.value}/ai-settings`); } catch (e) { err.value = e.message; }
}
async function saveAiSettings() {
  err.value = ''; aiSaving.value = true;
  try { aiSettings.value = await api.put(`/api/org-admin/${orgId.value}/ai-settings`, aiSettings.value); }
  catch (e) { err.value = e.message; }
  finally { aiSaving.value = false; }
}

async function loadAiProvider() {
  err.value = '';
  try {
    aiProvider.value = await api.get(`/api/org-admin/${orgId.value}/ai-provider`);
    aiProviderForm.value = { apiKey: '', baseUrl: aiProvider.value.baseUrl || '', model: aiProvider.value.model || '', generateModel: aiProvider.value.generateModel || '' };
  } catch (e) { err.value = e.message; }
}
async function saveAiProvider() {
  err.value = ''; aiProviderSaving.value = true; aiProviderMsg.value = '';
  try {
    aiProvider.value = await api.put(`/api/org-admin/${orgId.value}/ai-provider`, aiProviderForm.value);
    aiProviderForm.value.apiKey = '';
    aiProviderMsg.value = 'Saved.';
  } catch (e) { err.value = e.message; }
  finally { aiProviderSaving.value = false; }
}
async function clearAiProviderKey() {
  if (!(await confirmDialog.ask('Clear this organization\'s saved API key? It will fall back to the platform default.', { confirmLabel: 'Clear' }))) return;
  err.value = '';
  try { await api.del(`/api/org-admin/${orgId.value}/ai-provider/api-key`); await loadAiProvider(); }
  catch (e) { err.value = e.message; }
}

async function loadLtiPlatforms() {
  err.value = '';
  try { ltiPlatforms.value = await api.get(`/api/org-admin/${orgId.value}/lti-platforms`); } catch (e) { err.value = e.message; }
}
async function loadLtiToolConfig() {
  try { ltiToolConfig.value = await api.get(`/api/org-admin/${orgId.value}/lti-platforms/tool-config`); } catch (e) { err.value = e.message; }
}
function startNewLtiPlatform() { ltiEditing.value = 'new'; ltiForm.value = blankLtiForm(); }
function startEditLtiPlatform(p) { ltiEditing.value = p.id; ltiForm.value = { ...p }; }
function cancelLtiEdit() { ltiEditing.value = null; }
async function saveLtiPlatform() {
  err.value = '';
  try {
    if (ltiEditing.value === 'new') await api.post(`/api/org-admin/${orgId.value}/lti-platforms`, ltiForm.value);
    else await api.put(`/api/org-admin/${orgId.value}/lti-platforms/${ltiEditing.value}`, ltiForm.value);
    ltiEditing.value = null;
    await loadLtiPlatforms();
  } catch (e) { err.value = e.message; }
}
async function deleteLtiPlatform(p) {
  if (!(await confirmDialog.ask(`Remove platform "${p.name}"? Existing linked users/boards stay, but it can no longer launch.`, { confirmLabel: 'Remove' }))) return;
  err.value = '';
  try { await api.del(`/api/org-admin/${orgId.value}/lti-platforms/${p.id}`); await loadLtiPlatforms(); }
  catch (e) { err.value = e.message; }
}
async function copyLtiValue(value) {
  try { await navigator.clipboard.writeText(value); ltiCopyMsg.value = 'Copied.'; setTimeout(() => (ltiCopyMsg.value = ''), 1500); }
  catch { /* clipboard permission denied — not worth surfacing an error for */ }
}

// URL normalization (missing/invalid slug or tab) happens inside selectOrg once the org
// list is known — see its router.replace check.
onMounted(() => { loadOrgs(); });
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-8">
    <div class="flex items-center gap-3 mb-4">
      <h1 class="text-xl font-bold">Organization</h1>
      <TableViewToggle class="ml-auto" />
    </div>

    <p v-if="err" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ err }}</p>

    <p v-if="orgs && !orgs.length" class="text-slate-400 dark:text-slate-500 text-sm">
      You don't administer any organization.
    </p>

    <template v-else-if="orgs">
      <div class="flex items-center gap-3 mb-4 flex-wrap">
        <select v-if="orgs.length > 1" :value="orgId" @change="selectOrg(Number($event.target.value))"
                class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1.5 text-sm">
          <option v-for="o in orgs" :key="o.id" :value="o.id">{{ o.name }}</option>
        </select>
        <h2 v-else class="font-semibold">{{ orgs[0]?.name }}</h2>
        <span v-if="summary" class="text-xs text-slate-400 dark:text-slate-500">
          {{ summary.memberCount }} member(s) · {{ summary.boardCount }} board(s)
        </span>
      </div>

      <!-- mobile: current tab + dropdown; desktop: inline pill row -->
      <div class="mb-4">
        <button @click="mobileTabsOpen = !mobileTabsOpen"
                class="md:hidden w-full flex items-center justify-between gap-2 rounded-lg border border-slate-300 dark:border-slate-700 px-3 py-2 text-sm font-medium">
          {{ tabDefs.find((t) => t[0] === tab)?.[1] }}
          <svg viewBox="0 0 24 24" class="w-4 h-4 shrink-0 transition-transform" :class="{ 'rotate-180': mobileTabsOpen }"
               fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 9l6 6 6-6" /></svg>
        </button>
        <div :class="mobileTabsOpen ? 'flex' : 'hidden'"
             class="md:flex flex-col md:flex-row flex-wrap gap-1 mt-1 md:mt-0 pb-2 border-b border-slate-200 dark:border-slate-800 text-sm">
          <button v-for="t in tabDefs" :key="t[0]"
                  @click="switchTab(t[0])" class="whitespace-nowrap text-left md:text-center rounded-lg px-3 py-1.5"
                  :class="tab === t[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
            {{ t[1] }}
          </button>
        </div>
      </div>

      <!-- Dashboard -->
      <section v-show="tab === 'dashboard'" class="space-y-5">
        <button @click="loadDashboard" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>

        <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
          <button type="button" @click="switchTab('members')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
            <div class="text-xs text-slate-400 dark:text-slate-500">Members</div>
            <div class="text-2xl font-bold">{{ fmt(dashboard.totalMembers) }}</div>
            <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
              {{ dashboard.teacherCount }} teacher · {{ dashboard.studentCount }} student · {{ dashboard.adminCount }} admin
            </div>
          </button>

          <button type="button" @click="switchTab('boards')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
            <div class="text-xs text-slate-400 dark:text-slate-500">Boards</div>
            <div class="text-2xl font-bold">{{ fmt(dashboard.totalBoards) }}</div>
            <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">{{ fmt(dashboard.totalProblems) }} problem(s)</div>
          </button>

          <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
            <div class="text-xs text-slate-400 dark:text-slate-500">Submissions</div>
            <div class="text-2xl font-bold">{{ fmt(dashboard.totalSubmissions) }}</div>
            <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
              {{ dashboard.totalSubmissions ? Math.round(100 * dashboard.acceptedSubmissions / dashboard.totalSubmissions) : 0 }}% accepted
            </div>
          </div>

          <button type="button" @click="switchTab('ai')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
            <div class="text-xs text-slate-400 dark:text-slate-500">AI calls today / this month</div>
            <div class="text-2xl font-bold">{{ fmt(dashboard.aiToday.calls) }} / {{ fmt(dashboard.aiMonth.calls) }}</div>
            <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
              {{ fmt(dashboard.aiToday.totalTokens) }} / {{ fmt(dashboard.aiMonth.totalTokens) }} tokens · across this org's members
            </div>
          </button>
        </div>
        <p v-else-if="!dashboard" class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>

        <div>
          <div class="flex items-center gap-2 mb-2">
            <h3 class="font-semibold text-sm">Engagement</h3>
            <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
              <button v-for="g in [['hour', 'Hourly'], ['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                      @click="setEngagementGranularity(g[0])" class="px-2.5 py-1"
                      :class="engagementGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
                {{ g[1] }}
              </button>
            </span>
          </div>
          <div v-if="engagementStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <MiniLineChart :title="`Active users / ${engagementGranularity}`" :points="activeUserPoints()" />
            <MiniLineChart :title="`Submissions / ${engagementGranularity}`" :points="submissionPoints()" />
          </div>
          <p v-else-if="engagementStats" class="text-slate-400 dark:text-slate-500 text-sm">No activity in this window yet.</p>
        </div>

        <div>
          <div class="flex items-center gap-2 mb-2">
            <h3 class="font-semibold text-sm">AI usage</h3>
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

        <div v-if="topicStats">
          <h2 class="font-semibold text-sm mb-1.5">Top topics by attempts</h2>
          <TopicBarChart :items="topicBarItems()" />
        </div>
      </section>

      <!-- Members -->
      <section v-show="tab === 'members'">
        <div class="flex flex-wrap gap-2 mb-3">
          <input v-model="newMemberEmail" @keyup.enter="addMember" placeholder="Email of an existing BeeCoding account"
                 class="flex-1 min-w-0 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
          <select v-model="newMemberRole" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
            <option value="Member">Member</option>
            <option value="Admin">Admin</option>
          </select>
          <button @click="addMember" class="text-sm bg-amber-500 text-white rounded-lg px-4 font-medium">Add</button>
        </div>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3 mb-3">
          <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400 mb-1">Bulk add (CSV)</h3>
          <p class="text-[11px] text-slate-400 dark:text-slate-500 mb-2">
            Header row with an <code>email</code> column (optional <code>role</code>: Member/Admin, default
            Member). Only existing BeeCoding accounts can be added — have others register first.
          </p>
          <input type="file" accept="text/csv,.csv" @change="importMembersCsv" :disabled="importingMembers" class="text-sm" />
          <p v-if="importingMembers" class="text-xs text-slate-400 mt-2">Importing…</p>
          <div v-if="memberImportResult" class="mt-2 text-xs space-y-1">
            <p class="text-emerald-600 dark:text-emerald-400">
              Added {{ memberImportResult.added }} · Skipped {{ memberImportResult.skipped }} · Errors {{ memberImportResult.errors }}
            </p>
            <ul class="space-y-0.5">
              <li v-for="(row, i) in memberImportResult.rows.filter((r) => r.message)" :key="i" class="text-slate-400 dark:text-slate-500">
                {{ row.email }}: {{ row.message }}
              </li>
            </ul>
          </div>
        </div>

        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2">
          <div v-for="m in members" :key="m.userId" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="flex items-start justify-between gap-2">
              <div class="min-w-0">
                <div class="font-medium text-sm truncate">{{ m.displayName }}</div>
                <div class="text-[11px] text-slate-400 truncate">{{ m.email }}</div>
              </div>
              <div class="flex items-center gap-1 shrink-0">
                <button @click="viewMemberSubmissions(m)" class="row-action-btn">Submissions</button>
                <button @click="removeMember(m)" class="row-action-btn row-action-btn--danger">Remove</button>
              </div>
            </div>
            <div class="flex items-center gap-2 mt-2">
              <select :value="m.orgRole" @change="changeMemberRole(m, $event.target.value)"
                      class="text-[11px] border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-1 py-0.5">
                <option value="Member">Member</option>
                <option value="Admin">Admin</option>
              </select>
              <span class="text-[11px] text-slate-400">joined {{ new Date(m.joinedAt).toLocaleDateString() }}</span>
            </div>
          </div>
          <p v-if="members && !members.length" class="text-slate-400 dark:text-slate-500 text-sm">No members yet.</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">Name / email</th><th class="font-normal pr-3">Role</th>
                <th class="font-normal pr-3">Joined</th><th class="font-normal pr-3"></th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="m in members" :key="m.userId" class="border-b border-slate-100 dark:border-slate-800/60">
                <td><div class="font-medium">{{ m.displayName }}</div><div class="text-[11px] text-slate-400">{{ m.email }}</div></td>
                <td>
                  <select :value="m.orgRole" @change="changeMemberRole(m, $event.target.value)"
                          class="text-[11px] border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-1 py-0.5">
                    <option value="Member">Member</option>
                    <option value="Admin">Admin</option>
                  </select>
                </td>
                <td class="text-[11px] text-slate-400">{{ new Date(m.joinedAt).toLocaleDateString() }}</td>
                <td class="whitespace-nowrap">
                  <button @click="viewMemberSubmissions(m)" class="row-action-btn">Submissions</button>
                  <button @click="removeMember(m)" class="row-action-btn row-action-btn--danger">Remove</button>
                </td>
              </tr>
              <tr v-if="members && !members.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No members yet.</td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Boards -->
      <section v-show="tab === 'boards'">
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3 mb-3">
          <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400 mb-1">Bulk-create boards</h3>
          <p class="text-[11px] text-slate-400 dark:text-slate-500 mb-2">
            One title per line — e.g. 13 session boards for one course. The owner must already have a
            Teacher account; if they're not a member of this org yet, adding their first board here enrolls them.
          </p>
          <input v-model="bulkBoardOwnerEmail" placeholder="Owner's email (existing Teacher account)"
                 class="w-full mb-2 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
          <textarea v-model="bulkBoardTitles" rows="4" placeholder="Session 1&#10;Session 2&#10;Session 3"
                    class="w-full mb-2 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm font-mono"></textarea>
          <button @click="bulkCreateBoards" :disabled="bulkBoardBusy || !bulkBoardOwnerEmail.trim() || !bulkBoardTitles.trim()"
                  class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ bulkBoardBusy ? 'Creating…' : 'Create boards' }}
          </button>
          <div v-if="bulkBoardResult" class="mt-2 text-xs space-y-1">
            <p class="text-emerald-600 dark:text-emerald-400">
              Created {{ bulkBoardResult.created }} · Errors {{ bulkBoardResult.errors }}
            </p>
            <ul class="space-y-0.5">
              <li v-for="(row, i) in bulkBoardResult.rows.filter((r) => r.message)" :key="i" class="text-slate-400 dark:text-slate-500">
                {{ row.title }}: {{ row.message }}
              </li>
            </ul>
          </div>
        </div>

        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2">
          <div v-for="b in boards" :key="b.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="font-medium text-sm">{{ b.title }}</div>
            <div class="text-[11px] text-slate-400">{{ b.ownerEmail }}</div>
            <div class="text-[11px] text-slate-400 mt-0.5">
              {{ b.memberCount }} student(s) · {{ b.problemCount }} problem(s) · created {{ new Date(b.createdAt).toLocaleDateString() }}
            </div>
            <div v-if="editingBoardTagsSlug === b.slug" class="flex items-center gap-1 mt-1.5">
              <input v-model="boardTagsInput" placeholder="e.g. semester-1, class-a" @keyup.enter="saveBoardTags(b)"
                     class="flex-1 text-xs border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
              <button @click="saveBoardTags(b)" class="row-action-btn row-action-btn--success">Save</button>
              <button @click="editingBoardTagsSlug = null" class="row-action-btn">Cancel</button>
            </div>
            <div v-else class="flex flex-wrap items-center gap-1 mt-1.5">
              <span v-for="t in (b.tags ? b.tags.split(',') : [])" :key="t"
                    class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
              <button @click="startEditBoardTags(b)" class="row-action-btn">{{ b.tags ? '✏️ Edit tags' : '+ Add tags' }}</button>
            </div>
          </div>
          <p v-if="boards && !boards.length" class="text-slate-400 dark:text-slate-500 text-sm">No boards yet.</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">Title</th><th class="font-normal pr-3">Owner</th>
                <th class="font-normal pr-3">Students</th><th class="font-normal pr-3">Problems</th><th class="font-normal pr-3">Created</th>
                <th class="font-normal pr-3">Tags</th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="b in boards" :key="b.id" class="border-b border-slate-100 dark:border-slate-800/60">
                <td class="font-medium">{{ b.title }}</td>
                <td class="text-[11px] text-slate-400">{{ b.ownerEmail }}</td>
                <td class="tabular-nums">{{ b.memberCount }}</td>
                <td class="tabular-nums">{{ b.problemCount }}</td>
                <td class="text-[11px] text-slate-400">{{ new Date(b.createdAt).toLocaleDateString() }}</td>
                <td class="min-w-48">
                  <div v-if="editingBoardTagsSlug === b.slug" class="flex items-center gap-1">
                    <input v-model="boardTagsInput" placeholder="e.g. semester-1, class-a" @keyup.enter="saveBoardTags(b)"
                           class="flex-1 text-xs border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
                    <button @click="saveBoardTags(b)" class="row-action-btn row-action-btn--success">Save</button>
                    <button @click="editingBoardTagsSlug = null" class="row-action-btn">Cancel</button>
                  </div>
                  <div v-else class="flex flex-wrap items-center gap-1">
                    <span v-for="t in (b.tags ? b.tags.split(',') : [])" :key="t"
                          class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
                    <button @click="startEditBoardTags(b)" class="row-action-btn">{{ b.tags ? '✏️ Edit' : '+ Add' }}</button>
                  </div>
                </td>
              </tr>
              <tr v-if="boards && !boards.length"><td colspan="6" class="text-slate-400 dark:text-slate-500 py-3">No boards yet.</td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Submissions -->
      <section v-show="tab === 'submissions'">
        <div class="flex flex-wrap items-center gap-2 mb-3">
          <input v-model="submissionQ" @keyup.enter="searchSubmissions" placeholder="Search student, email, problem, or board…"
                 class="flex-1 min-w-0 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
          <select v-model="submissionSource" @change="searchSubmissions"
                  class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
            <option value="">Board + Practice</option>
            <option value="board">Board only</option>
            <option value="practice">Practice only</option>
          </select>
          <select v-model="submissionVerdict" @change="searchSubmissions"
                  class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
            <option value="">Any verdict</option>
            <option value="Accepted">Accepted</option>
            <option value="WrongAnswer">Wrong answer</option>
            <option value="TimeLimit">Time limit</option>
            <option value="MemoryLimit">Memory limit</option>
            <option value="RuntimeError">Runtime error</option>
            <option value="CompileError">Compile error</option>
          </select>
          <button @click="searchSubmissions" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">Search</button>
          <button @click="loadSubmissions" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>
        </div>
        <div v-if="submissionUserId" class="flex items-center gap-2 mb-3 text-xs">
          <span class="text-slate-500 dark:text-slate-400">Filtered to</span>
          <span class="px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800">{{ submissionUserLabel }}</span>
          <button @click="clearSubmissionUserFilter" class="row-action-btn">✕ clear</button>
        </div>

        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2">
          <button v-for="s in submissionRows" :key="`${s.source}-${s.id}`" @click="openSubmission(s)"
                  class="w-full text-left border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="flex items-center justify-between gap-2 mb-1">
              <VerdictBadge :verdict="s.verdict" small />
              <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ new Date(s.createdAt).toLocaleString() }}</span>
            </div>
            <div class="text-sm font-medium">{{ s.problemTitle }}</div>
            <div class="text-[11px] text-slate-400 mt-0.5">
              {{ s.userDisplayName }} ({{ s.userEmail }}) · {{ s.source === 'Board' ? s.boardTitle : 'Practice' }}
            </div>
          </button>
          <p v-if="submissionRows && !submissionRows.length" class="text-slate-400 dark:text-slate-500 text-sm">No submissions.</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">When</th><th class="font-normal pr-3">Student</th>
                <th class="font-normal pr-3">Problem</th><th class="font-normal pr-3">Board / Source</th>
                <th class="font-normal pr-3">Verdict</th><th class="font-normal pr-3">Score</th>
                <th class="font-normal pr-3">Runtime</th><th class="font-normal pr-3">Lang</th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="s in submissionRows" :key="`${s.source}-${s.id}`" @click="openSubmission(s)"
                  class="border-b border-slate-100 dark:border-slate-800/60 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40">
                <td class="text-[11px] text-slate-400 whitespace-nowrap">{{ new Date(s.createdAt).toLocaleString() }}</td>
                <td><div class="font-medium">{{ s.userDisplayName }}</div><div class="text-[11px] text-slate-400">{{ s.userEmail }}</div></td>
                <td>{{ s.problemTitle }}</td>
                <td class="text-[11px] text-slate-400">{{ s.source === 'Board' ? s.boardTitle : 'Practice' }}</td>
                <td><VerdictBadge :verdict="s.verdict" small /></td>
                <td class="tabular-nums">{{ Math.round(s.score * 100) }}%</td>
                <td class="text-[11px] text-slate-400 tabular-nums">{{ s.runtimeMs }}ms</td>
                <td class="text-[11px] text-slate-400">{{ s.language || '—' }}</td>
              </tr>
              <tr v-if="submissionRows && !submissionRows.length"><td colspan="8" class="text-slate-400 dark:text-slate-500 py-3">No submissions.</td></tr>
            </tbody>
          </table>
        </div>
        <div v-if="submissionsTotal" class="flex items-center gap-3 mt-3 text-sm">
          <span class="text-slate-400 dark:text-slate-500">
            {{ (submissionsPage - 1) * submissionsPageSize + 1 }}–{{ Math.min(submissionsPage * submissionsPageSize, submissionsTotal) }} of {{ submissionsTotal }}
          </span>
          <div class="ml-auto flex gap-2">
            <button @click="submissionsPrevPage" :disabled="submissionsPage === 1"
                    class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
            <button @click="submissionsNextPage" :disabled="submissionsPage * submissionsPageSize >= submissionsTotal"
                    class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
          </div>
        </div>

        <SubmissionView v-if="viewSubmission" :submission-id="viewSubmission.id" :source="viewSubmission.source"
                        :author-name="viewSubmission.authorName" @close="viewSubmission = null" />
      </section>

      <!-- Plagiarism -->
      <section v-show="tab === 'plagiarism'">
        <div class="flex items-center gap-2 mb-3">
          <select v-model="plagiarismBoardId" @change="loadPlagiarism"
                  class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
            <option :value="null">Pick a board…</option>
            <option v-for="b in boards" :key="b.id" :value="b.id">{{ b.title }}</option>
          </select>
        </div>
        <PlagiarismTable :pairs="plagiarismPairs" :loading="plagiarismLoading" @view="plagiarismViewSubmission = $event" />
        <SubmissionView v-if="plagiarismViewSubmission" :submission-id="plagiarismViewSubmission.id"
                        :author-name="plagiarismViewSubmission.authorName" @close="plagiarismViewSubmission = null" />
      </section>

      <!-- AI settings -->
      <section v-show="tab === 'ai'" class="max-w-sm space-y-3">
        <p class="text-xs text-slate-400 dark:text-slate-500">
          These settings only take effect once your organization has its own AI API key
          <em>and</em> base URL set below — until then, your members use (and are bound by)
          the platform's own pause and quota, and this quota field is ignored. Bring your own
          key and this org becomes independent of the platform's pause entirely.
        </p>
        <template v-if="aiSettings">
          <label class="flex items-center gap-2 text-sm">
            <input type="checkbox" v-model="aiSettings.paused" /> Pause AI for this organization
          </label>
          <input v-if="aiSettings.paused" v-model="aiSettings.pausedReason" placeholder="Reason (shown to users)"
                 class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
          <label class="flex items-center gap-2 text-sm">Daily quota — students
            <input v-model.number="aiSettings.dailyQuotaStudent" type="number" min="0"
                   class="w-20 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label class="flex items-center gap-2 text-sm">Daily quota — teachers
            <input v-model.number="aiSettings.dailyQuotaTeacher" type="number" min="0"
                   class="w-20 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <button @click="saveAiSettings" :disabled="aiSaving"
                  class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ aiSaving ? 'Saving…' : 'Save' }}
          </button>
        </template>

        <div class="border-t border-slate-200 dark:border-slate-800 pt-3 mt-4">
          <h3 class="font-semibold text-sm mb-1">AI provider (bring your own key)</h3>
          <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">
            Leave everything blank to keep using the platform default.
          </p>
          <template v-if="aiProvider">
            <label class="flex flex-col gap-1 text-sm mb-2">
              <span class="text-xs text-slate-400 dark:text-slate-500">
                API key {{ aiProvider.hasApiKey ? `(saved: ${aiProvider.apiKeyPreview})` : '(none — using the platform default)' }}
              </span>
              <div class="flex gap-2">
                <input v-model="aiProviderForm.apiKey" type="password" placeholder="Leave blank to keep the saved key"
                       class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
                <button v-if="aiProvider.hasApiKey" @click="clearAiProviderKey" class="row-action-btn row-action-btn--danger shrink-0">Clear</button>
              </div>
            </label>
            <input v-model="aiProviderForm.baseUrl" placeholder="Base URL (blank = platform default)"
                   class="w-full mb-2 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
            <input v-model="aiProviderForm.model" placeholder="Model (blank = platform default)"
                   class="w-full mb-2 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
            <input v-model="aiProviderForm.generateModel" placeholder="Generate-problem model (blank = same as above)"
                   class="w-full mb-2 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
            <p v-if="aiProviderMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mb-2">{{ aiProviderMsg }}</p>
            <button @click="saveAiProvider" :disabled="aiProviderSaving"
                    class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
              {{ aiProviderSaving ? 'Saving…' : 'Save' }}
            </button>
          </template>
        </div>
      </section>

      <!-- LTI: this org's own platform registrations only -->
      <section v-show="tab === 'lti'" class="space-y-5">
        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <h2 class="font-semibold text-sm mb-1">Tool configuration</h2>
          <p class="text-xs text-slate-400 dark:text-slate-500 mb-3">
            Give these to whoever registers BeeCoding as an external tool in your LMS. The
            LMS gives back an issuer, a client ID, and its own login/token/JWKS URLs —
            register the platform below with those.
          </p>
          <div v-if="ltiToolConfig" class="space-y-1.5 text-xs">
            <div v-for="[label, value] in [
              ['OIDC login initiation URL', ltiToolConfig.loginInitiationUrl],
              ['Launch / redirect URL', ltiToolConfig.launchUrl],
              ['Public JWKS URL', ltiToolConfig.jwksUrl],
              ['Deep Linking URL', ltiToolConfig.deepLinkingUrl],
            ]" :key="label" class="flex items-center gap-2">
              <span class="w-40 shrink-0 text-slate-400 dark:text-slate-500">{{ label }}</span>
              <code class="flex-1 min-w-0 truncate bg-slate-100 dark:bg-slate-800 rounded px-2 py-1">{{ value }}</code>
              <button @click="copyLtiValue(value)" class="shrink-0 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-200" title="Copy">⧉</button>
            </div>
            <p v-if="ltiCopyMsg" class="text-emerald-600 dark:text-emerald-400">{{ ltiCopyMsg }}</p>
          </div>
        </div>

        <div>
          <div class="flex items-center gap-2 mb-2">
            <h2 class="font-semibold text-sm">Registered platforms</h2>
            <button @click="startNewLtiPlatform" class="ml-auto text-xs bg-amber-500 text-white rounded-lg px-3 py-1 font-medium">+ Add platform</button>
          </div>

          <div v-if="ltiEditing" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 mb-3 space-y-2 text-sm">
            <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400">{{ ltiEditing === 'new' ? 'New platform' : 'Edit platform' }}</h3>
            <input v-model="ltiForm.name" placeholder="Name (e.g. Moodle)" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <input v-model="ltiForm.issuer" placeholder="Issuer (iss)" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <input v-model="ltiForm.clientId" placeholder="Client ID" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <input v-model="ltiForm.deploymentIds" placeholder="Deployment ID(s), comma-separated" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <input v-model="ltiForm.authLoginUrl" placeholder="Auth login URL" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <input v-model="ltiForm.authTokenUrl" placeholder="Auth token URL" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <input v-model="ltiForm.jwksUrl" placeholder="JWKS (key set) URL" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
            <p class="text-[11px] text-slate-400 dark:text-slate-500">
              Launches through this platform auto-join this organization and auto-assign new boards to it.
            </p>
            <label class="flex items-center gap-2 text-xs"><input type="checkbox" v-model="ltiForm.enabled" /> Enabled</label>
            <div class="flex gap-2 pt-1">
              <button @click="saveLtiPlatform" class="bg-amber-500 text-white rounded-lg px-4 py-1.5 text-sm font-medium">Save</button>
              <button @click="cancelLtiEdit" class="text-slate-500 dark:text-slate-400 px-3 text-sm">Cancel</button>
            </div>
          </div>

          <!-- mobile: cards -->
          <div v-if="tableView === 'card'" class="space-y-2">
            <div v-for="p in ltiPlatforms" :key="p.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
              <div class="flex items-start justify-between gap-2">
                <div class="font-medium text-sm">{{ p.name }}</div>
                <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap shrink-0"
                      :class="p.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300'">
                  {{ p.enabled ? 'enabled' : 'disabled' }}
                </span>
              </div>
              <div class="text-[11px] text-slate-400 truncate mt-1">{{ p.issuer }}</div>
              <div class="text-[11px] text-slate-400 truncate">{{ p.clientId }}</div>
              <div class="mt-2 flex gap-3">
                <button @click="startEditLtiPlatform(p)" class="row-action-btn row-action-btn--accent">Edit</button>
                <button @click="deleteLtiPlatform(p)" class="row-action-btn row-action-btn--danger">Remove</button>
              </div>
            </div>
            <p v-if="ltiPlatforms && !ltiPlatforms.length" class="text-slate-400 dark:text-slate-500 text-sm">No platforms registered yet.</p>
          </div>

          <!-- desktop: table -->
          <div v-if="tableView === 'table'" class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                  <th class="font-normal py-1.5 pr-3">Name</th><th class="font-normal pr-3">Issuer</th>
                  <th class="font-normal pr-3">Client ID</th>
                  <th class="font-normal pr-3">Status</th><th class="font-normal pr-3"></th>
                </tr>
              </thead>
              <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
                <tr v-for="p in ltiPlatforms" :key="p.id" class="border-b border-slate-100 dark:border-slate-800/60">
                  <td class="font-medium">{{ p.name }}</td>
                  <td class="text-[11px] text-slate-400 max-w-40 truncate">{{ p.issuer }}</td>
                  <td class="text-[11px] text-slate-400 max-w-32 truncate">{{ p.clientId }}</td>
                  <td>
                    <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap"
                          :class="p.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300'">
                      {{ p.enabled ? 'enabled' : 'disabled' }}
                    </span>
                  </td>
                  <td class="whitespace-nowrap">
                    <button @click="startEditLtiPlatform(p)" class="row-action-btn row-action-btn--accent mr-1">Edit</button>
                    <button @click="deleteLtiPlatform(p)" class="row-action-btn row-action-btn--danger">Remove</button>
                  </td>
                </tr>
                <tr v-if="ltiPlatforms && !ltiPlatforms.length"><td colspan="5" class="text-slate-400 dark:text-slate-500 py-3">No platforms registered yet.</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>
