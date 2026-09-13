<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import { useConfirmDialog } from '../stores/confirmDialog';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';

const confirmDialog = useConfirmDialog();

const orgs = ref(null);
const orgId = ref(null);
const tabDefs = [['dashboard', 'Dashboard'], ['members', 'Members'], ['boards', 'Boards'], ['ai', 'AI settings'], ['lti', 'LTI']];
const tab = ref('dashboard');
const mobileTabsOpen = ref(false);
const err = ref('');

const summary = ref(null);
const dashboard = ref(null);
const weeklyStats = ref(null);
const topicStats = ref(null);
const members = ref(null);
const boards = ref(null);
const aiSettings = ref(null);
const aiSaving = ref(false);
const aiProvider = ref(null);
const aiProviderForm = ref({ apiKey: '', baseUrl: '', model: '', generateModel: '' });
const aiProviderSaving = ref(false);
const aiProviderMsg = ref('');

const newMemberEmail = ref('');
const newMemberRole = ref('Member');

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
    if (orgs.value.length && !orgId.value) selectOrg(orgs.value[0].id);
  } catch (e) { err.value = e.message; }
}

function selectOrg(id) {
  orgId.value = id;
  summary.value = null; dashboard.value = null; weeklyStats.value = null; topicStats.value = null;
  members.value = null; boards.value = null; aiSettings.value = null; aiProvider.value = null;
  ltiPlatforms.value = null; ltiToolConfig.value = null; ltiEditing.value = null;
  loadTab(tab.value);
}

function switchTab(id) { mobileTabsOpen.value = false; tab.value = id; loadTab(id); }
function loadTab(id) {
  if (!orgId.value) return;
  loadSummary();
  if (id === 'dashboard' && !dashboard.value) loadDashboard();
  else if (id === 'members' && !members.value) loadMembers();
  else if (id === 'boards' && !boards.value) loadBoards();
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
    const [d, weekly, topics] = await Promise.all([
      api.get(`/api/org-admin/${orgId.value}/dashboard`),
      api.get(`/api/org-admin/${orgId.value}/dashboard/weekly?weeks=12`),
      api.get(`/api/org-admin/${orgId.value}/dashboard/topics?take=8`),
    ]);
    dashboard.value = d;
    weeklyStats.value = weekly;
    topicStats.value = topics;
  } catch (e) { err.value = e.message; }
}
const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const fmt = (n) => (n ?? 0).toLocaleString();
const activeUserPoints = () => (weeklyStats.value || []).map((w) => ({ label: shortDate(w.weekStart), value: w.activeUsers }));
const submissionPoints = () => (weeklyStats.value || []).map((w) => ({ label: shortDate(w.weekStart), value: w.submissions }));
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

async function loadBoards() {
  err.value = '';
  try { boards.value = await api.get(`/api/org-admin/${orgId.value}/boards`); } catch (e) { err.value = e.message; }
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

onMounted(loadOrgs);
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-8">
    <h1 class="text-xl font-bold mb-4">Organization</h1>

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

        <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-3 gap-3">
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
            <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">across this org's members</div>
          </button>
        </div>
        <p v-else-if="!dashboard" class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>

        <div v-if="weeklyStats?.length" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart title="Active users / week" :points="activeUserPoints()" />
          <MiniLineChart title="Submissions / week" :points="submissionPoints()" />
        </div>

        <div v-if="topicStats">
          <h2 class="font-semibold text-sm mb-1.5">Top topics by attempts</h2>
          <TopicBarChart :items="topicBarItems()" />
        </div>
      </section>

      <!-- Members -->
      <section v-show="tab === 'members'">
        <div class="flex gap-2 mb-3">
          <input v-model="newMemberEmail" @keyup.enter="addMember" placeholder="Email of an existing BeeCoding account"
                 class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
          <select v-model="newMemberRole" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
            <option value="Member">Member</option>
            <option value="Admin">Admin</option>
          </select>
          <button @click="addMember" class="text-sm bg-amber-500 text-white rounded-lg px-4 font-medium">Add</button>
        </div>
        <div class="overflow-x-auto">
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
                <td><button @click="removeMember(m)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Remove</button></td>
              </tr>
              <tr v-if="members && !members.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No members yet.</td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Boards -->
      <section v-show="tab === 'boards'">
        <div class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">Title</th><th class="font-normal pr-3">Owner</th>
                <th class="font-normal pr-3">Students</th><th class="font-normal pr-3">Problems</th><th class="font-normal pr-3">Created</th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="b in boards" :key="b.id" class="border-b border-slate-100 dark:border-slate-800/60">
                <td class="font-medium">{{ b.title }}</td>
                <td class="text-[11px] text-slate-400">{{ b.ownerEmail }}</td>
                <td class="tabular-nums">{{ b.memberCount }}</td>
                <td class="tabular-nums">{{ b.problemCount }}</td>
                <td class="text-[11px] text-slate-400">{{ new Date(b.createdAt).toLocaleDateString() }}</td>
              </tr>
              <tr v-if="boards && !boards.length"><td colspan="5" class="text-slate-400 dark:text-slate-500 py-3">No boards yet.</td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- AI settings -->
      <section v-show="tab === 'ai'" class="max-w-sm space-y-3">
        <p class="text-xs text-slate-400 dark:text-slate-500">
          Scoped to this organization only — never overrides the platform-wide AI pause.
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
                <button v-if="aiProvider.hasApiKey" @click="clearAiProviderKey" class="text-xs text-rose-600 dark:text-rose-400 hover:underline shrink-0">Clear</button>
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

          <div class="overflow-x-auto">
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
                    <span class="text-[11px] px-1.5 py-0.5 rounded-full"
                          :class="p.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300'">
                      {{ p.enabled ? 'enabled' : 'disabled' }}
                    </span>
                  </td>
                  <td class="whitespace-nowrap">
                    <button @click="startEditLtiPlatform(p)" class="text-[11px] text-violet-600 dark:text-violet-400 hover:underline mr-3">Edit</button>
                    <button @click="deleteLtiPlatform(p)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Remove</button>
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
