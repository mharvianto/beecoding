<script setup>
import { tr } from '../lib/i18n';
import Icon from '../components/Icon.vue';
import { hasData } from '../lib/hasData';
import { ref, reactive, toRef, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import { useAuth } from '../stores/auth';
import { useUndoToast } from '../stores/undoToast';
import { useConfirmDialog } from '../stores/confirmDialog';
import AdminUserActions from '../components/AdminUserActions.vue';
import SortTh from '../components/SortTh.vue';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';
import MarkdownBlock from '../components/MarkdownBlock.vue';
import LevelBadge from '../components/LevelBadge.vue';
import ImageUsagePanel from '../components/ImageUsagePanel.vue';
import AiCheckPanel from '../components/AiCheckPanel.vue';
import SimilarProblems from '../components/SimilarProblems.vue';
import TableViewToggle from '../components/TableViewToggle.vue';
import SubmissionView from '../components/SubmissionView.vue';
import VerdictBadge from '../components/VerdictBadge.vue';
import PlagiarismTable from '../components/PlagiarismTable.vue';
import SubmissionDiffView from '../components/SubmissionDiffView.vue';
import { useUrlQuery, lastPage } from '../lib/urlQuery';
import BulkAddPanel from '../components/BulkAddPanel.vue';
import AdminNotifyKinds from '../components/AdminNotifyKinds.vue';
import { tableView } from '../lib/tableView';

const auth = useAuth();
const route = useRoute();
const router = useRouter();
const confirmDialog = useConfirmDialog();
const undoToast = useUndoToast();
const tabDefs = [
  ['dashboard', 'Dashboard'], ['ai', 'AI'], ['users', 'Users'], ['boards', 'Boards'], ['problems', 'Problems'],
  ['submissions', 'Submissions'], ['plagiarism', 'Plagiarism'], ['review', 'Problem review'], ['reports', 'Reports'], ['trash', 'Trash'],
  ['audit', 'Audit log'], ['organizations', 'Organizations'], ['lti', 'LTI'],
];
// Deep-linkable: /admin/users etc. — reload/share/bookmark lands on the same tab.
const tab = ref(tabDefs.some((t) => t[0] === route.params.tab) ? route.params.tab : 'dashboard');
const err = ref('');

const aiRows = ref(null);
const aiPage = ref(1);
const aiPageSize = ref(25);
const aiTotal = ref(0);
const users = ref(null);
const userQ = ref('');
const userFilter = ref('');
const usersSort = ref('');      // '' = by id
const usersDir = ref('asc');
const aiSort = ref('');         // '' = all-time tokens, highest first
const aiDir = ref('desc');
// Clicking a column sorts by it (text A→Z, numbers high→low first); clicking the active column flips the direction.
const TEXT_COLUMNS = new Set(['name', 'role', 'user']);
const firstDir = (field, idFirst) => (TEXT_COLUMNS.has(field) || (field === '' && idFirst) ? 'asc' : 'desc');
function nextSort(sortRef, dirRef, field, idFirst) {
  if (sortRef.value === field) dirRef.value = dirRef.value === 'asc' ? 'desc' : 'asc';
  else { sortRef.value = field; dirRef.value = firstDir(field, idFirst); }
}
const USER_SORTS = [['', '#'], ['name', 'Name'], ['role', 'Role'], ['xp', 'XP'], ['boards', 'Boards'], ['subs', 'Subs'], ['joined', 'Joined']];
const AI_SORTS = [['', 'All time'], ['user', 'User'], ['today', 'Today'], ['month', 'This month']];
const userStats = ref(null);
// Sign-in security headcount chips above the users table: each one filters the list to who is (or isn't) covered.
const SECURITY_CHIPS = [
  { key: 'verified', label: 'Email verified', stat: 'emailVerified', miss: 'unverified', missLabel: 'unverified' },
  { key: 'mfa', label: '2FA on', stat: 'mfa', miss: 'no-mfa', missLabel: 'without' },
  { key: 'passkey', label: 'Passkey', stat: 'passkey' },
];
const usersPage = ref(1);
const usersPageSize = ref(25);
const usersTotal = ref(0);

const fmt = (n) => (n ?? 0).toLocaleString();
const when = (d) => new Date(d.endsWith('Z') ? d : d + 'Z').toLocaleString();
const actionBadgeClass = (action) => ({
  'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300': ['delete', 'admin-revoke', 'ai-ban', 'ai-pause', 'problem-review-reject'].includes(action),
  'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300': ['restore', 'create', 'ai-resume', 'problem-review-approve'].includes(action),
  'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300': ['purge', 'ai-override-clear'].includes(action),
  'bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300': ['admin-grant', 'role-change', 'ai-quota-set'].includes(action),
});

// `force` drops the cached rows of the filtered tabs so they refetch with the filters just read
// from the URL (back/forward or a pasted link), instead of showing the previous tab state.
function loadTabData(id, force = false) {
  if (force) { users.value = null; submissionRows.value = null; auditRows.value = null; aiRows.value = null; }
  if (id === 'dashboard') { if (!dashboard.value) loadDashboard(); }
  else if (id === 'ai') {
    if (!aiRows.value) loadAi();
    if (!aiSettings.value) loadAiSettings();
    if (!aiProvider.value) loadAiProvider();
    if (!aiOverrides.value) loadAiOverrides();
  } else if (id === 'users') {
    if (!users.value) loadUsers();
    if (!mfaPolicy.value) loadMfaPolicy();
    if (!boards.value) loadBoards();   // populates the CSV-import board picker too
  } else if (id === 'boards' && !boards.value) loadBoards();
  else if (id === 'submissions' && !submissionRows.value) loadSubmissions();
  else if (id === 'plagiarism') {
    if (!boards.value) loadBoards();
    if (plagiarismBoardId.value) { loadPlagiarismProblems(); loadPlagiarism(); }   // ?board=&problem= from a shared link
  }
  else if (id === 'review') loadAiReview();   // queue changes often — always refresh
  else if (id === 'reports') { if (!systemStatus.value) loadSystemStatus(); loadImageUsage(); }
  else if (id === 'trash') loadTrash();     // state changes often — always refresh
  else if (id === 'audit' && !auditRows.value) loadAudit();
  else if (id === 'organizations' && !organizations.value) loadOrganizations();
  else if (id === 'lti') {
    if (!ltiPlatforms.value) loadLtiPlatforms();
    if (!ltiToolConfig.value) loadLtiToolConfig();
    if (!organizations.value) loadOrganizations();   // for the platform-org picker
  }
}
const mobileTabsOpen = ref(false);
function switchTab(id) {
  mobileTabsOpen.value = false;
  if (id === tab.value) return;
  tab.value = id;
  url.write();   // this tab's filters -> URL (and the path moves to /admin/<id>)
  loadTabData(id);
}
// Browser back/forward (or a direct link to /admin/<tab>) changes route.params.tab
// without going through switchTab — keep the active tab (and its data) in sync.
watch(() => route.params.tab, (t) => {
  const id = tabDefs.some(([k]) => k === t) ? t : 'dashboard';
  if (id !== tab.value) { tab.value = id; url.read(); loadTabData(id, true); }
});

// ---- dashboard: at-a-glance overview, the default landing tab ----
const dashboard = ref(null);
const topicStats = ref(null);
const ENGAGEMENT_GRANULARITY_KEY = 'beecoding.admin.engagementGranularity';
const AI_GRANULARITY_KEY = 'beecoding.admin.aiGranularity';
const engagementGranularity = ref(localStorage.getItem(ENGAGEMENT_GRANULARITY_KEY) || 'week');   // 'hour' | 'day' | 'week'
const engagementStats = ref(null);
const aiGranularity = ref(localStorage.getItem(AI_GRANULARITY_KEY) || 'week');                    // 'day' | 'week' — AiUsage has no finer data than a day
const aiEngagementStats = ref(null);

async function loadDashboard() {
  err.value = '';
  try {
    const [d, topics] = await Promise.all([
      api.get('/api/admin-ui/dashboard'),
      api.get('/api/admin-ui/dashboard/topics?take=8'),
    ]);
    dashboard.value = d;
    topicStats.value = topics;
    await Promise.all([loadEngagement(), loadAiEngagement()]);
  } catch (e) { err.value = e.message; }
}

function periodsFor(granularity) {
  return granularity === 'hour' ? 168 : granularity === 'day' ? 90 : 12;   // the server's own caps for hour/day
}
async function loadEngagement() {
  err.value = '';
  try {
    engagementStats.value = await api.get(
      `/api/admin-ui/dashboard/engagement?granularity=${engagementGranularity.value}&periods=${periodsFor(engagementGranularity.value)}`);
  } catch (e) { err.value = e.message; }
}
function setEngagementGranularity(g) {
  engagementGranularity.value = g;
  try { localStorage.setItem(ENGAGEMENT_GRANULARITY_KEY, g); } catch { /* ignore */ }
  loadEngagement();
}

async function loadAiEngagement() {
  err.value = '';
  try {
    aiEngagementStats.value = await api.get(
      `/api/admin-ui/dashboard/ai-engagement?granularity=${aiGranularity.value}&periods=${periodsFor(aiGranularity.value)}`);
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

const activeUserPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.activeUsers }));
const submissionPoints = () => (engagementStats.value || []).map((w) => ({ label: engagementLabel(w.periodStart), value: w.submissions }));
const aiCallPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.calls }));
const aiTokenPoints = () => (aiEngagementStats.value || []).map((w) => ({ label: shortDate(w.periodStart), value: w.totalTokens }));
const topicBarItems = () => (topicStats.value || []).map((t) => ({ label: t.tag, value: t.attempts, rate: t.acceptRate }));

// ---- AI settings: kill-switch, quotas, per-user overrides ----
const aiSettings = ref(null);
const aiSettingsSaving = ref(false);
const aiOverrides = ref(null);
const newOverride = ref({ userId: null, quota: null, banned: false });

async function loadAiSettings() {
  err.value = '';
  try { aiSettings.value = await api.get('/api/admin-ui/ai-settings'); }
  catch (e) { err.value = e.message; }
}
async function saveAiSettings() {
  err.value = ''; aiSettingsSaving.value = true;
  try { aiSettings.value = await api.put('/api/admin-ui/ai-settings', aiSettings.value); }
  catch (e) { err.value = e.message; }
  finally { aiSettingsSaving.value = false; }
}
// ---- AI provider/credential: platform default ----
const aiProvider = ref(null);
const aiProviderForm = ref({ apiKey: '', baseUrl: '', model: '', generateModel: '' });
const aiProviderSaving = ref(false);
const aiProviderMsg = ref('');
async function loadAiProvider() {
  err.value = '';
  try {
    aiProvider.value = await api.get('/api/admin-ui/ai-provider');
    loadAiExtras();
    loadHintEffect();
    aiProviderForm.value = { apiKey: '', baseUrl: aiProvider.value.baseUrl || '', model: aiProvider.value.model || '', generateModel: aiProvider.value.generateModel || '' };
  } catch (e) { err.value = e.message; }
}
async function saveAiProvider() {
  err.value = ''; aiProviderSaving.value = true; aiProviderMsg.value = '';
  try {
    aiProvider.value = await api.put('/api/admin-ui/ai-provider', aiProviderForm.value);
    aiProviderForm.value.apiKey = '';
    aiProviderMsg.value = 'Saved.';
  } catch (e) { err.value = e.message; }
  finally { aiProviderSaving.value = false; }
}
// ---- AI tuning + backup providers (tried in order when the main provider fails) ----
const aiExtras = ref(null);
const aiExtrasForm = ref({ maxTokens: '', reasoningEffort: '', reviewModel: '', jobEffort: '', fallbacks: [] });
const aiExtrasSaving = ref(false);
const aiExtrasMsg = ref('');
function fillAiExtras(x) {
  aiExtras.value = x;
  aiExtrasForm.value = {
    maxTokens: x.maxTokens ?? '', reasoningEffort: x.reasoningEffort || '', reviewModel: x.reviewModel || '', jobEffort: x.jobEffort || '',
    fallbacks: x.fallbacks.map((f) => ({ id: f.id, apiKey: '', keyPreview: f.keyPreview, baseUrl: f.baseUrl || '', model: f.model })),
  };
}
async function loadAiExtras() {
  try { fillAiExtras(await api.get('/api/admin-ui/ai-provider/extras')); } catch (e) { err.value = e.message; }
}
// ---- Are AI hints helping? ----
const hintEffect = ref(null);
const hintEffectDays = ref(30);
async function loadHintEffect() {
  try { hintEffect.value = await api.get(`/api/admin-ui/ai-hint-effect?days=${hintEffectDays.value}`); } catch (e) { err.value = e.message; }
}
const pct = (v) => (v == null ? '–' : `${v}%`);
function addAiFallback() { aiExtrasForm.value.fallbacks.push({ id: null, apiKey: '', keyPreview: null, baseUrl: '', model: '' }); }
async function saveAiExtras() {
  err.value = ''; aiExtrasSaving.value = true; aiExtrasMsg.value = '';
  try {
    const f = aiExtrasForm.value;
    fillAiExtras(await api.put('/api/admin-ui/ai-provider/extras', {
      maxTokens: f.maxTokens === '' || f.maxTokens === null ? null : Number(f.maxTokens),
      reasoningEffort: f.reasoningEffort || null,
      reviewModel: f.reviewModel || null,
      jobEffort: f.jobEffort || null,
      fallbacks: f.fallbacks.map((x) => ({ id: x.id, apiKey: x.apiKey, baseUrl: x.baseUrl, model: x.model })),
    }));
    aiExtrasMsg.value = 'Saved.';
  } catch (e) { err.value = e.message; }
  finally { aiExtrasSaving.value = false; }
}
async function clearAiProviderKey() {
  if (!(await confirmDialog.ask(tr('Clear the saved API key? AI will fall back to appsettings.json\'s key (if any).'), { confirmLabel: tr('Clear') }))) return;
  err.value = '';
  try { await api.del('/api/admin-ui/ai-provider/api-key'); await loadAiProvider(); }
  catch (e) { err.value = e.message; }
}

async function loadAiOverrides() {
  err.value = '';
  try { aiOverrides.value = await api.get('/api/admin-ui/ai-settings/overrides'); }
  catch (e) { err.value = e.message; }
}
async function saveOverride(row) {
  err.value = '';
  try {
    await api.put(`/api/admin-ui/ai-settings/overrides/${row.userId}`, { dailyQuotaOverride: row.dailyQuotaOverride, banned: row.banned });
    await loadAiOverrides();
  } catch (e) { err.value = e.message; }
}
async function clearOverride(row) {
  err.value = '';
  try { await api.del(`/api/admin-ui/ai-settings/overrides/${row.userId}`); await loadAiOverrides(); }
  catch (e) { err.value = e.message; }
}
async function addOverride() {
  if (!newOverride.value.userId) return;
  err.value = '';
  try {
    await api.put(`/api/admin-ui/ai-settings/overrides/${newOverride.value.userId}`, {
      dailyQuotaOverride: newOverride.value.quota, banned: newOverride.value.banned,
    });
    newOverride.value = { userId: null, quota: null, banned: false };
    await loadAiOverrides();
  } catch (e) { err.value = e.message; }
}

// ---- AI review queue: AI-generated problems held back until approved ----
const aiReview = ref(null);
const expandedReview = ref(null);   // slug of the row currently expanded
const rejectReasonDraft = ref({});  // slug -> reason text while rejecting

async function loadAiReview() {
  err.value = '';
  try { aiReview.value = await api.get('/api/admin-ui/ai-review'); }
  catch (e) { err.value = e.message; }
}
async function approveAiReview(row) {
  err.value = '';
  try {
    await api.post(`/api/admin-ui/ai-review/${row.slug}/approve`);
    aiReview.value = aiReview.value.filter((r) => r.slug !== row.slug);
  } catch (e) { err.value = e.message; }
}
async function deleteAiReview(row) {
  if (!(await confirmDialog.ask(tr('Delete “{p0}”? It moves to Trash, where you can restore or purge it.', { p0: row.title }), { confirmLabel: tr('Delete') }))) return;
  err.value = '';
  try {
    await api.del(`/api/bank/${row.slug}`);
    aiReview.value = aiReview.value.filter((r) => r.slug !== row.slug);
  } catch (e) { err.value = e.message; }
}
async function rejectAiReview(row) {
  err.value = '';
  try {
    await api.post(`/api/admin-ui/ai-review/${row.slug}/reject`, { reason: rejectReasonDraft.value[row.slug] || null });
    aiReview.value = aiReview.value.filter((r) => r.slug !== row.slug);
    delete rejectReasonDraft.value[row.slug];
  } catch (e) { err.value = e.message; }
}

async function loadAi() {
  err.value = '';
  url.write();
  try {
    const p = new URLSearchParams({ page: String(aiPage.value), pageSize: String(aiPageSize.value) });
    if (aiSort.value) p.set('sort', aiSort.value);
    p.set('dir', aiDir.value);
    const result = await api.get(`/api/admin-ui/ai-usage?${p}`);
    aiRows.value = result.rows;
    aiTotal.value = result.total;
    const last = lastPage(result.total, aiPageSize.value);
    if (aiPage.value > last) { aiPage.value = last; await loadAi(); }
  } catch (e) { err.value = e.message; }
}
function aiPrevPage() { if (aiPage.value > 1) { aiPage.value--; loadAi(); } }
function aiNextPage() { if (aiPage.value * aiPageSize.value < aiTotal.value) { aiPage.value++; loadAi(); } }
async function loadUsers() {
  err.value = '';
  selectedUsers.value = new Set();
  url.write();
  try {
    const p = new URLSearchParams({ page: String(usersPage.value), pageSize: String(usersPageSize.value) });
    if (userQ.value.trim()) p.set('q', userQ.value.trim());
    if (userFilter.value) p.set('filter', userFilter.value);
    if (usersSort.value) p.set('sort', usersSort.value);
    p.set('dir', usersDir.value);
    const result = await api.get(`/api/admin-ui/users?${p}`);
    users.value = result.rows;
    userStats.value = result.stats;
    usersTotal.value = result.total;
    const last = lastPage(result.total, usersPageSize.value);
    if (usersPage.value > last) { usersPage.value = last; await loadUsers(); }
  } catch (e) { err.value = e.message; }
}
function sortUsers(field) { nextSort(usersSort, usersDir, field, true); usersPage.value = 1; loadUsers(); }
function sortAi(field) { nextSort(aiSort, aiDir, field, false); aiPage.value = 1; loadAi(); }
function setUserFilter(f) { userFilter.value = userFilter.value === f ? '' : f; usersPage.value = 1; loadUsers(); }
function searchUsers() { usersPage.value = 1; loadUsers(); }
function usersPrevPage() { if (usersPage.value > 1) { usersPage.value--; loadUsers(); } }
function usersNextPage() { if (usersPage.value * usersPageSize.value < usersTotal.value) { usersPage.value++; loadUsers(); } }

async function markVerified(u) {
  err.value = '';
  try { await api.post(`/api/admin-ui/users/${u.id}/verify-email`); u.emailVerified = true; } catch (e) { err.value = e.message; }
}

// ---- two-step verification policy: which roles must use it ----
const mfaPolicy = ref(null);   // { requireAdmin, requireOrgAdmin, requireTeacher, adminsWithout, orgAdminsWithout, teachersWithout }
const mfaPolicyBusy = ref(false);
const MFA_GROUPS = [
  { key: 'requireAdmin', without: 'adminsWithout', label: 'Admins' },
  { key: 'requireOrgAdmin', without: 'orgAdminsWithout', label: 'Organization admins' },
  { key: 'requireTeacher', without: 'teachersWithout', label: 'Teachers' },
];
async function loadMfaPolicy() {
  try { mfaPolicy.value = await api.get('/api/admin-ui/mfa-policy'); } catch (e) { err.value = e.message; }
}
async function toggleMfaPolicy(g) {
  const p = mfaPolicy.value;
  const turningOn = !p[g.key];
  if (turningOn && p[g.without] > 0
      && !(await confirmDialog.ask(
        tr('{p0} {p1} don\'t have two-step verification yet. They will be held on their Account page until they turn it on. Continue?', { p0: p[g.without], p1: g.label.toLowerCase() }),
        { confirmLabel: tr('Require it'), danger: false }))) return;
  err.value = '';
  mfaPolicyBusy.value = true;
  try {
    mfaPolicy.value = await api.put('/api/admin-ui/mfa-policy', { ...p, [g.key]: turningOn });
  } catch (e) { err.value = e.message; } finally { mfaPolicyBusy.value = false; }
}

// ---- remove a user's second factor (lost device + lost recovery codes) ----
async function resetMfa(u) {
  err.value = '';
  if (!(await confirmDialog.ask(tr('Remove two-step verification for {p0}? They will sign in with just their password.', { p0: u.email }), { confirmLabel: tr('Reset 2FA') }))) return;
  try { await api.post(`/api/admin-ui/users/${u.id}/reset-mfa`); u.mfaEnabled = false; } catch (e) { err.value = e.message; }
}

// ---- one-off password-reset link for a user (works without outgoing email) ----
const resetLink = ref(null);   // { user, url, expiresAt }
const resetLinkCopied = ref(false);
async function issueResetLink(u) {
  err.value = '';
  try {
    const r = await api.post(`/api/admin-ui/users/${u.id}/reset-link`);
    resetLink.value = { user: u, url: r.url, expiresAt: r.expiresAt };
    resetLinkCopied.value = false;
  } catch (e) { err.value = e.message; }
}
async function copyResetLink() {
  try { await navigator.clipboard.writeText(resetLink.value.url); resetLinkCopied.value = true; } catch { /* select it manually */ }
}

async function deleteUser(u) {
  err.value = '';
  try {
    await api.del(`/api/admin-ui/users/${u.id}`);
    await loadUsers();
    undoToast.show(`"${u.displayName}" deleted.`, async () => {
      await api.post(`/api/admin-ui/users/${u.id}/restore`);
      await loadUsers();
    });
  } catch (e) { err.value = e.message; }
}

// ---- bulk-delete users ----
const selectedUsers = ref(new Set());
function toggleUserSelect(id) {
  const next = new Set(selectedUsers.value);
  next.has(id) ? next.delete(id) : next.add(id);
  selectedUsers.value = next;
}
function selectAllUsers(checked) {
  selectedUsers.value = checked
    ? new Set((users.value || []).filter((u) => u.id !== auth.user?.id).map((u) => u.id))
    : new Set();
}
const userDeleteResultMsg = ref('');
async function deleteSelectedUsers() {
  const ids = [...selectedUsers.value];
  if (!ids.length) return;
  if (!(await confirmDialog.ask(tr('Delete {p0} user(s)? They\'ll move to Trash and can be restored there.', { p0: ids.length }), { confirmLabel: tr('Delete') }))) return;
  err.value = ''; userDeleteResultMsg.value = '';
  try {
    const result = await api.post('/api/admin-ui/users/bulk-delete', { ids });
    selectedUsers.value = new Set();
    await loadUsers();
    userDeleteResultMsg.value = tr('Deleted {p0} user(s).', { p0: result.deleted }) + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
  } catch (e) { err.value = e.message; }
}

// ---- recalculate streak/best-day from raw submission history ----
const recalcBusy = ref(false);
const recalcResultMsg = ref('');
async function recalculateProgress() {
  if (!(await confirmDialog.ask(
    tr('Recalculate every user\'s streak, longest streak and best-day-solved from their raw submission history? This overwrites the current values.'),
    { confirmLabel: tr('Recalculate') }
  ))) return;
  err.value = ''; recalcResultMsg.value = ''; recalcBusy.value = true;
  try {
    const result = await api.post('/api/admin-ui/users/recalculate-progress');
    await loadUsers();
    recalcResultMsg.value = tr('Recalculated {p0} user(s).', { p0: result.updated });
  } catch (e) { err.value = e.message; }
  finally { recalcBusy.value = false; }
}

async function changeRole(u, role) {
  if (role === u.role) return;
  err.value = '';
  try {
    await api.patch(`/api/admin-ui/users/${u.id}/role`, { role });
    u.role = role;
  } catch (e) { err.value = e.message; }
}
async function grantAdmin(u) {
  err.value = '';
  try { await api.post(`/api/admin-ui/users/${u.id}/admin`); u.isAdmin = true; }
  catch (e) { err.value = e.message; }
}
async function revokeAdmin(u) {
  err.value = '';
  try { await api.del(`/api/admin-ui/users/${u.id}/admin`); u.isAdmin = false; }
  catch (e) { err.value = e.message; }
}

// ---- bulk user import (CSV) ----
const importingUsers = ref(false);
const userImportResult = ref(null);
const importBoardSlug = ref('');
const importDefaultRole = ref('Student');

async function importUsersCsv(ev) {
  const file = ev.target.files?.[0];
  ev.target.value = '';
  if (!file) return;
  err.value = ''; userImportResult.value = null; importingUsers.value = true;
  try {
    const csv = await file.text();
    userImportResult.value = await api.post('/api/admin-ui/users/import', {
      csv, boardSlug: importBoardSlug.value || null, defaultRole: importDefaultRole.value,
    });
    if (users.value) await loadUsers();
  } catch (e) { err.value = e.message; }
  finally { importingUsers.value = false; }
}

// ---- boards (browse all) ----
const boards = ref(null);
const boardQ = ref('');
async function loadBoards() {
  err.value = '';
  try { boards.value = await api.get('/api/admin-ui/boards' + (boardQ.value.trim() ? `?q=${encodeURIComponent(boardQ.value.trim())}` : '')); }
  catch (e) { err.value = e.message; }
}
const plagiarismBoardId = ref(null);
const plagiarismProblemId = ref(null);   // null = every problem on the board
const plagiarismProblems = ref([]);
const plagiarismPairs = ref({ groups: [], pairs: [] });
const plagiarismLoading = ref(false);
const plagiarismViewSubmission = ref(null);
const plagiarismCompareData = ref(null);
async function loadPlagiarismProblems() {
  plagiarismProblems.value = [];
  if (!plagiarismBoardId.value) return;
  try { plagiarismProblems.value = await api.get(`/api/admin-ui/plagiarism/problems?boardId=${plagiarismBoardId.value}`); }
  catch (e) { err.value = e.message; }
}
// A different board has different problems: start over on "all problems".
async function pickPlagiarismBoard() {
  plagiarismProblemId.value = null;
  await loadPlagiarismProblems();
  loadPlagiarism();
}
async function loadPlagiarism() {
  url.write();
  if (!plagiarismBoardId.value) { plagiarismPairs.value = { groups: [], pairs: [] }; return; }
  err.value = ''; plagiarismLoading.value = true;
  try { plagiarismPairs.value = await api.get(`/api/admin-ui/plagiarism?boardId=${plagiarismBoardId.value}${plagiarismProblemId.value ? `&problemId=${plagiarismProblemId.value}` : ''}`); }
  catch (e) { err.value = e.message; }
  finally { plagiarismLoading.value = false; }
}

async function deleteBoard(b) {
  err.value = '';
  try {
    await api.del(`/api/boards/${b.slug}`);
    boards.value = boards.value.filter((x) => x.slug !== b.slug);
    undoToast.show(`"${b.title}" deleted.`, async () => {
      await api.post(`/api/boards/${b.slug}/restore`);
      await loadBoards();
    });
  } catch (e) { err.value = e.message; }
}

// ---- bulk-archive boards (e.g. "everything from last semester") ----
const selectedBoards = ref(new Set());
const archiveBeforeDate = ref('');
function toggleBoardSelect(slug) {
  const next = new Set(selectedBoards.value);
  next.has(slug) ? next.delete(slug) : next.add(slug);
  selectedBoards.value = next;
}
function selectAllBoards(checked) {
  selectedBoards.value = checked ? new Set((boards.value || []).map((b) => b.slug)) : new Set();
}
function selectBoardsBeforeDate() {
  if (!archiveBeforeDate.value) return;
  const cutoff = new Date(archiveBeforeDate.value).getTime();
  selectedBoards.value = new Set((boards.value || []).filter((b) => new Date(b.createdAt).getTime() < cutoff).map((b) => b.slug));
}
const archiveResultMsg = ref('');
const bulkAdding = ref(false);
async function archiveSelectedBoards() {
  const slugs = [...selectedBoards.value];
  if (!slugs.length) return;
  if (!(await confirmDialog.ask(tr('Archive {p0} board(s)? They\'ll move to Trash and can be restored there (no 30s limit for admins).', { p0: slugs.length }), { confirmLabel: tr('Archive') }))) return;
  err.value = ''; archiveResultMsg.value = '';
  try {
    const result = await api.post('/api/admin-ui/boards/archive', { slugs });
    selectedBoards.value = new Set();
    await loadBoards();
    archiveResultMsg.value = tr('Archived {p0} board(s).', { p0: result.archived }) + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
  } catch (e) { err.value = e.message; }
}

// ---- trash: browse + restore/purge soft-deleted rows (no time limit for admins) ----
// One category at a time, each independently paginated/selectable — the underlying tables
// (esp. deleted problems) can grow large enough that loading everything at once doesn't scale.
const TRASH_KINDS = [
  ['users', 'Users', (r) => r.displayName],
  ['boards', 'Boards', (r) => r.title],
  ['problems', 'Problems', (r) => `${r.title} (${r.boardTitle})`],
  ['bank', 'Bank problems', (r) => r.title],
];
const trash = reactive(Object.fromEntries(TRASH_KINDS.map(([kind]) => [kind, {
  rows: null, total: 0, page: 1, pageSize: 25, selected: new Set(), msg: '',
}])));
const trashKey = (kind, row) => (kind === 'users' ? String(row.id) : row.slug);

async function loadTrashKind(kind) {
  err.value = '';
  const st = trash[kind];
  url.write();
  try {
    const p = new URLSearchParams({ page: String(st.page), pageSize: String(st.pageSize) });
    const result = await api.get(`/api/admin-ui/trash/${kind}?${p}`);
    st.rows = result.rows;
    st.total = result.total;
    st.selected = new Set();
    const last = lastPage(result.total, st.pageSize);
    if (st.page > last) { st.page = last; await loadTrashKind(kind); }
  } catch (e) { err.value = e.message; }
}
function loadTrash() { return Promise.all(TRASH_KINDS.map(([kind]) => loadTrashKind(kind))); }

function toggleTrashSelect(kind, row) {
  const id = trashKey(kind, row);
  const next = new Set(trash[kind].selected);
  next.has(id) ? next.delete(id) : next.add(id);
  trash[kind].selected = next;
}
function selectAllTrash(kind, checked) {
  trash[kind].selected = checked ? new Set((trash[kind].rows || []).map((r) => trashKey(kind, r))) : new Set();
}
function trashPrevPage(kind) { const st = trash[kind]; if (st.page > 1) { st.page--; loadTrashKind(kind); } }
function trashNextPage(kind) { const st = trash[kind]; if (st.page * st.pageSize < st.total) { st.page++; loadTrashKind(kind); } }

async function restoreTrash(kind, row) {
  err.value = '';
  try {
    if (kind === 'users') await api.post(`/api/admin-ui/users/${row.id}/restore`);
    else if (kind === 'boards') await api.post(`/api/boards/${row.slug}/restore`);
    else if (kind === 'problems') await api.post(`/api/boards/${row.boardSlug}/problems/${row.slug}/restore`);
    else if (kind === 'bank') await api.post(`/api/bank/${row.slug}/restore`);
    await loadTrashKind(kind);
  } catch (e) { err.value = e.message; }
}
async function purge(kind, row, label) {
  if (!(await confirmDialog.ask(tr('Permanently delete "{p0}"? This cannot be undone.', { p0: label }), { confirmLabel: tr('Delete forever') }))) return;
  err.value = '';
  try {
    await api.del(`/api/admin-ui/trash/${kind}/${kind === 'users' ? row.id : row.slug}`);
    await loadTrashKind(kind);
  } catch (e) { err.value = e.message; }
}

async function restoreSelectedTrash(kind) {
  const ids = [...trash[kind].selected];
  if (!ids.length) return;
  err.value = ''; trash[kind].msg = '';
  try {
    const result = await api.post(`/api/admin-ui/trash/${kind}/restore`, { ids });
    await loadTrashKind(kind);
    trash[kind].msg = `Restored ${result.count}.` + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
  } catch (e) { err.value = e.message; }
}
async function purgeSelectedTrash(kind) {
  const ids = [...trash[kind].selected];
  if (!ids.length) return;
  if (!(await confirmDialog.ask(tr('Permanently delete {p0} item(s)? This cannot be undone.', { p0: ids.length }), { confirmLabel: tr('Delete forever') }))) return;
  err.value = ''; trash[kind].msg = '';
  try {
    const result = await api.post(`/api/admin-ui/trash/${kind}/purge`, { ids });
    await loadTrashKind(kind);
    trash[kind].msg = `Purged ${result.count}.` + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
  } catch (e) { err.value = e.message; }
}

// ---- submissions: every submission, platform-wide — board AND practice ----
const submissionRows = ref(null);
const submissionQ = ref('');
const submissionVerdict = ref('');
const submissionSource = ref('');
const submissionsPage = ref(1);
const submissionsPageSize = ref(50);
const submissionsTotal = ref(0);
const viewSubmission = ref(null);   // { id, source, authorName } | null

async function loadSubmissions() {
  err.value = '';
  url.write();
  try {
    const p = new URLSearchParams({ page: String(submissionsPage.value), pageSize: String(submissionsPageSize.value) });
    if (submissionQ.value.trim()) p.set('q', submissionQ.value.trim());
    if (submissionVerdict.value) p.set('verdict', submissionVerdict.value);
    if (submissionSource.value) p.set('source', submissionSource.value);
    const result = await api.get(`/api/admin-ui/submissions?${p}`);
    submissionRows.value = result.rows;
    submissionsTotal.value = result.total;
    const last = lastPage(result.total, submissionsPageSize.value);
    if (submissionsPage.value > last) { submissionsPage.value = last; await loadSubmissions(); }
  } catch (e) { err.value = e.message; }
}
function searchSubmissions() { submissionsPage.value = 1; loadSubmissions(); }
function submissionsPrevPage() { if (submissionsPage.value > 1) { submissionsPage.value--; loadSubmissions(); } }
function submissionsNextPage() { if (submissionsPage.value * submissionsPageSize.value < submissionsTotal.value) { submissionsPage.value++; loadSubmissions(); } }
function openSubmission(s) { viewSubmission.value = { id: s.id, source: s.source.toLowerCase(), authorName: s.userDisplayName }; }
function problemLink(s) {
  if (!s.problemSlug) return null;
  return s.source === 'Board' ? `/boards/${s.boardSlug}/problems/${s.problemSlug}` : `/practice/${s.problemSlug}`;
}

// ---- audit log ----
const auditRows = ref(null);
const auditQ = ref('');
const auditPage = ref(1);
const auditPageSize = ref(50);
const auditTotal = ref(0);

async function loadAudit() {
  err.value = '';
  url.write();
  try {
    const p = new URLSearchParams({ page: String(auditPage.value), pageSize: String(auditPageSize.value) });
    if (auditQ.value.trim()) p.set('q', auditQ.value.trim());
    const result = await api.get(`/api/admin-ui/audit-log?${p}`);
    auditRows.value = result.rows;
    auditTotal.value = result.total;
    const last = lastPage(result.total, auditPageSize.value);
    if (auditPage.value > last) { auditPage.value = last; await loadAudit(); }
  } catch (e) { err.value = e.message; }
}
function searchAudit() { auditPage.value = 1; loadAudit(); }
function auditPrevPage() { if (auditPage.value > 1) { auditPage.value--; loadAudit(); } }
function auditNextPage() { if (auditPage.value * auditPageSize.value < auditTotal.value) { auditPage.value++; loadAudit(); } }

// ---- Organizations (multi-tenant) ----
const organizations = ref(null);
const orgForm = ref({ name: '', slug: '' });
async function loadOrganizations() {
  err.value = '';
  try { organizations.value = await api.get('/api/admin-ui/organizations'); }
  catch (e) { err.value = e.message; }
}
async function createOrganization() {
  if (!orgForm.value.name.trim() || !orgForm.value.slug.trim()) return;
  err.value = '';
  try {
    await api.post('/api/admin-ui/organizations', orgForm.value);
    orgForm.value = { name: '', slug: '' };
    await loadOrganizations();
  } catch (e) { err.value = e.message; }
}
const orgEdit = ref(null);   // { id, name, slug } while a row is being edited
function startOrgEdit(o) { orgEdit.value = { id: o.id, name: o.name, slug: o.slug, emailDomains: o.emailDomains || '' }; }
async function saveOrgEdit() {
  const e = orgEdit.value;
  if (!e || !e.name.trim() || !e.slug.trim()) return;
  err.value = '';
  try {
    await api.put(`/api/admin-ui/organizations/${e.id}`, { name: e.name, slug: e.slug, emailDomains: e.emailDomains });
    orgEdit.value = null;
    await loadOrganizations();
  } catch (x) { err.value = x.message; }
}
async function deleteOrganization(o) {
  if (!(await confirmDialog.ask(tr('Delete organization "{p0}"? Its boards/LTI platforms stay but become unaffiliated; members lose Org Admin access to it.', { p0: o.name }), { confirmLabel: tr('Delete') }))) return;
  err.value = '';
  try { await api.del(`/api/admin-ui/organizations/${o.id}`); await loadOrganizations(); }
  catch (e) { err.value = e.message; }
}

// ---- LTI 1.3 platform registry ----
const ltiPlatforms = ref(null);
const ltiToolConfig = ref(null);
const ltiEditing = ref(null);   // id of the platform being edited, or 'new'
const ltiForm = ref({});
const ltiCopyMsg = ref('');
const blankLtiForm = () => ({ name: '', issuer: '', clientId: '', deploymentIds: '', authLoginUrl: '', authTokenUrl: '', jwksUrl: '', enabled: true, organizationId: null });

async function loadLtiPlatforms() {
  err.value = '';
  try { ltiPlatforms.value = await api.get('/api/admin-ui/lti-platforms'); }
  catch (e) { err.value = e.message; }
}
async function loadLtiToolConfig() {
  err.value = '';
  try { ltiToolConfig.value = await api.get('/api/admin-ui/lti-platforms/tool-config'); }
  catch (e) { err.value = e.message; }
}
function startNewLtiPlatform() { ltiEditing.value = 'new'; ltiForm.value = blankLtiForm(); }
function startEditLtiPlatform(p) { ltiEditing.value = p.id; ltiForm.value = { ...p }; }
function cancelLtiEdit() { ltiEditing.value = null; }
async function saveLtiPlatform() {
  err.value = '';
  try {
    if (ltiEditing.value === 'new') await api.post('/api/admin-ui/lti-platforms', ltiForm.value);
    else await api.put(`/api/admin-ui/lti-platforms/${ltiEditing.value}`, ltiForm.value);
    ltiEditing.value = null;
    await loadLtiPlatforms();
  } catch (e) { err.value = e.message; }
}
async function deleteLtiPlatform(p) {
  if (!(await confirmDialog.ask(tr('Remove platform "{p0}"? Existing linked users/boards stay, but it can no longer launch.', { p0: p.name }), { confirmLabel: tr('Remove') }))) return;
  err.value = '';
  try { await api.del(`/api/admin-ui/lti-platforms/${p.id}`); await loadLtiPlatforms(); }
  catch (e) { err.value = e.message; }
}
async function copyLtiValue(value) {
  try { await navigator.clipboard.writeText(value); ltiCopyMsg.value = 'Copied.'; setTimeout(() => (ltiCopyMsg.value = ''), 1500); }
  catch { /* clipboard permission denied — not worth surfacing an error for */ }
}

// ---- problems export / import ----
const importing = ref(false);
const importResult = ref(null);
const replaceExisting = ref(true);

// A problem waiting for review, in the same bundle format as Problems > Export, so it can be copied elsewhere or
// loaded again through Problems > Import. isPublic stays false: importing must not publish something not yet approved.
// Pictures in the statement are referenced by id (/api/images/...), so they only resolve on this server.
const reviewJson = (row) => JSON.stringify({
  version: 1,
  exportedAt: new Date().toISOString(),
  problems: [{
    ownerEmail: row.ownerEmail, title: row.title, statementMarkdown: row.statementMarkdown,
    allowedLanguages: row.allowedLanguages, level: row.level, tags: row.tags,
    timeLimitMs: row.timeLimitMs, memoryLimitKb: row.memoryLimitKb, isPublic: false, generatedByAi: row.generatedByAi,
    bannedHeaders: row.bannedHeaders ?? null, bannedSymbols: row.bannedSymbols ?? null, inputFileName: row.inputFileName ?? null,
    tests: row.tests.map((t, i) => ({ stdin: t.stdin, expectedStdout: t.expectedStdout, isSample: t.isSample, points: t.points ?? 1, position: t.position ?? i })),
  }],
}, null, 2);
const reviewJsonCopied = ref(null);   // slug of the row whose JSON was just copied
async function copyReviewJson(row) {
  try {
    await navigator.clipboard.writeText(reviewJson(row));
    reviewJsonCopied.value = row.slug;
    setTimeout(() => { if (reviewJsonCopied.value === row.slug) reviewJsonCopied.value = null; }, 1500);
  } catch { err.value = tr('Could not copy — use Download JSON instead.'); }
}
function downloadReviewJson(row) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([reviewJson(row)], { type: 'application/json' }));
  const d = new Date(), pad = (n) => String(n).padStart(2, '0');   // local time, like the rest of the page
  const stamp = `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
  const name = (row.title || 'problem').replace(/[^\w-]+/g, '-').replace(/^-+|-+$/g, '').toLowerCase() || 'problem';
  a.download = `${name}-${stamp}.json`;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

async function downloadFile(url, fallbackName) {
  err.value = '';
  try {
    const res = await fetch(url.startsWith('/') ? withBase(url) : url, { credentials: 'include' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const blob = await res.blob();
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = (res.headers.get('content-disposition') || '').match(/filename=([^;]+)/)?.[1] || fallbackName;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  } catch (e) { err.value = e.message; }
}
// Export: filters narrow the bank, `batch` problems go in each JSON file of the ZIP (see AdminUiController.Export).
const exportOpts = ref({ level: '', visibility: '', tag: '', owner: '', since: '', batch: 100, format: 'zip' });
const exporting = ref(false);
async function exportProblems() {
  const o = exportOpts.value;
  const p = new URLSearchParams({ format: o.format, batch: String(o.batch) });
  if (o.level) p.set('level', o.level);
  if (o.visibility) p.set('public', o.visibility === 'public' ? 'true' : 'false');
  if (o.tag.trim()) p.set('tag', o.tag.trim());
  if (o.owner.trim()) p.set('owner', o.owner.trim());
  if (o.since) p.set('since', o.since);
  err.value = ''; exporting.value = true;
  try { await downloadFile(`/api/admin-ui/problems/export?${p}`, `beecoding-problems.${o.format}`); }
  catch (e) { err.value = e.message; }
  finally { exporting.value = false; }
}

// ---- reports: analytics export + system status ----
const systemStatus = ref(null);
const imageUsage = ref(null);
async function loadImageUsage() {
  try { imageUsage.value = await api.get('/api/admin-ui/image-usage'); } catch { /* the panel just stays hidden */ }
}
async function loadSystemStatus() {
  err.value = '';
  try {
    systemStatus.value = await api.get('/api/admin-ui/system-status');
    if (systemStatus.value.sysstatInstalled) { loadSysstat(); loadSysstatDaily(); }
    loadLatency();
  } catch (e) { err.value = e.message; }
}

// ---- judge latency: time from submit to verdict, per hour (queue wait + compile + run) ----
const LATENCY_KEY = 'beecoding.admin.judgeLatencyHours';
const latencyHours = ref(48);
try { const v = Number(localStorage.getItem(LATENCY_KEY)); if ([24, 48, 168].includes(v)) latencyHours.value = v; } catch { /* ignore */ }
const latency = ref(null);   // { hours, slots, pendingNow, total, p95Seconds, buckets: [{ hourUtc, count, avgSeconds, p95Seconds, maxSeconds }] }
async function loadLatency() {
  try { latency.value = await api.get(`/api/admin-ui/judge-latency?hours=${latencyHours.value}`); }
  catch (e) { err.value = e.message; }
}
function setLatencyHours(h) {
  latencyHours.value = h;
  try { localStorage.setItem(LATENCY_KEY, String(h)); } catch { /* ignore */ }
  loadLatency();
}
const hourLabel = (d) => new Date(d.endsWith('Z') ? d : d + 'Z')
  .toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
const latencyTone = (s) => (s >= 30 ? 'text-rose-600 dark:text-rose-400 font-medium' : s >= 10 ? 'text-amber-700 dark:text-amber-400' : '');

// ---- sysstat (sar) CPU/memory history — this one instance's own host only ----
const sysstatMetric = ref('cpu');   // 'cpu' | 'mem'
const sysstatResult = ref(null);
const sysstatErr = ref('');
const sysstatDaysAgo = ref(0);
async function loadSysstat() {
  sysstatErr.value = '';
  try {
    sysstatResult.value = await api.get(`/api/admin-ui/system/sysstat?metric=${sysstatMetric.value}&daysAgo=${sysstatDaysAgo.value}`);
  } catch (e) { sysstatErr.value = e.message; }
}
const sysstatDaily = ref(null);
// how far back each bucket size reaches (hours of data are many points, so fewer days)
const SYSSTAT_BUCKETS = [['hour', 'Per hour', 3], ['6h', tr('Per 6 hours'), 7], ['day', 'Per day', 30]];
const SYSSTAT_BUCKET_KEY = 'beecoding.admin.sysstatBucket';
const sysstatBucket = ref((() => { try { return localStorage.getItem(SYSSTAT_BUCKET_KEY) || 'day'; } catch { return 'day'; } })());
async function loadSysstatDaily() {
  const days = (SYSSTAT_BUCKETS.find((b) => b[0] === sysstatBucket.value) || SYSSTAT_BUCKETS[2])[2];
  try { sysstatDaily.value = await api.get(`/api/admin-ui/system/sysstat/daily?metric=${sysstatMetric.value}&days=${days}&bucket=${sysstatBucket.value}`); }
  catch { sysstatDaily.value = null; }
}
function setSysstatBucket(b) {
  sysstatBucket.value = b;
  try { localStorage.setItem(SYSSTAT_BUCKET_KEY, b); } catch { /* ignore */ }
  loadSysstatDaily();
}
function setSysstatMetric(m) { sysstatMetric.value = m; loadSysstat(); loadSysstatDaily(); }
const dailyPoints = (key) => (sysstatDaily.value?.days || []).map((d) => ({ label: sysstatBucket.value === 'day' ? shortDate(d.start) : hourLabel(d.start), value: d[key] }));
const shortTime = (s) => new Date(s).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
const sysstatPoints = () => (sysstatResult.value?.points || []).map((p) => ({ label: shortTime(p.time), value: p.value }));

// ---- runtime config: the handful of judge/LSP knobs safe to flip without a restart ----
const runtimeConfigForm = ref({ lspEnabled: false, lspShared: false, judgeRateLimitMs: 1500, studentMaxProblems: 50, imageMaxKb: 1024, imageMaxPerUser: 100, notificationReadRetentionDays: 7, boardCodeRetentionDays: 365, practiceCodeRetentionDays: 180 });
const runtimeConfigSaving = ref(false);
const runtimeConfigMsg = ref('');
watch(systemStatus, (s) => {
  if (s) runtimeConfigForm.value = { lspEnabled: s.lspEnabled, lspShared: s.lspShared, judgeRateLimitMs: s.judgeRateLimitMs, studentMaxProblems: s.studentMaxProblems, imageMaxKb: s.imageMaxKb, imageMaxPerUser: s.imageMaxPerUser, notificationReadRetentionDays: s.notificationReadRetentionDays, boardCodeRetentionDays: s.boardCodeRetentionDays, practiceCodeRetentionDays: s.practiceCodeRetentionDays };
});
async function saveRuntimeConfig() {
  err.value = ''; runtimeConfigMsg.value = ''; runtimeConfigSaving.value = true;
  try {
    const r = await api.put('/api/admin-ui/runtime-config', runtimeConfigForm.value);
    if (systemStatus.value) { systemStatus.value.lspEnabled = r.lspEnabled; systemStatus.value.lspShared = r.lspShared; systemStatus.value.judgeRateLimitMs = r.judgeRateLimitMs; systemStatus.value.studentMaxProblems = r.studentMaxProblems; systemStatus.value.imageMaxKb = r.imageMaxKb; systemStatus.value.imageMaxPerUser = r.imageMaxPerUser; systemStatus.value.notificationReadRetentionDays = r.notificationReadRetentionDays; systemStatus.value.boardCodeRetentionDays = r.boardCodeRetentionDays; systemStatus.value.practiceCodeRetentionDays = r.practiceCodeRetentionDays; }
    runtimeConfigMsg.value = tr('Saved — takes effect immediately, no restart needed.');
  } catch (e) { err.value = e.message; }
  finally { runtimeConfigSaving.value = false; }
}

async function importProblems(ev) {
  const file = ev.target.files?.[0];
  ev.target.value = '';
  if (!file) return;
  err.value = ''; importResult.value = null; importing.value = true;
  try {
    importResult.value = /\.zip$/i.test(file.name)
      ? await api.postFile(`/api/admin-ui/problems/import-zip?replaceExisting=${replaceExisting.value}`, file)
      : await api.post(`/api/admin-ui/problems/import?replaceExisting=${replaceExisting.value}`, JSON.parse(await file.text()));
  } catch (e) { err.value = e.message; }
  finally { importing.value = false; }
}

// Each filtered tab keeps its search / filters / page in the URL (?q=&page=…) so a view survives
// refresh and can be shared. The fields depend on the active tab; the path is passed explicitly
// so a tab switch and the load that follows it can't race on the not-yet-updated route.
const url = useUrlQuery(() => {
  switch (tab.value) {
    case 'ai': return { sort: { ref: aiSort, def: '' }, dir: { ref: aiDir, def: 'desc' }, page: { ref: aiPage, def: 1, int: true } };
    case 'users': return { q: { ref: userQ, def: '' }, filter: { ref: userFilter, def: '' }, sort: { ref: usersSort, def: '' }, dir: { ref: usersDir, def: 'asc' }, page: { ref: usersPage, def: 1, int: true } };
    case 'submissions': return {
      q: { ref: submissionQ, def: '' },
      source: { ref: submissionSource, def: '' },
      verdict: { ref: submissionVerdict, def: '' },
      page: { ref: submissionsPage, def: 1, int: true },
    };
    case 'audit': return { q: { ref: auditQ, def: '' }, page: { ref: auditPage, def: 1, int: true } };
    case 'plagiarism': return { board: { ref: plagiarismBoardId, def: null, int: true }, problem: { ref: plagiarismProblemId, def: null, int: true } };
    case 'trash': return Object.fromEntries(TRASH_KINDS.map(([kind]) => [kind, { ref: toRef(trash[kind], 'page'), def: 1, int: true }]));
    default: return {};
  }
}, {
  path: () => `/admin/${tab.value}`,
  active: () => route.params.tab === tab.value,
  onExternalChange: () => loadTabData(tab.value, true),
});

onMounted(async () => {
  if (!auth.user?.isAdmin) { router.replace('/boards'); return; }
  // A bare /admin (or an unknown tab) is normalised to /admin/<tab>, keeping any query.
  if (route.params.tab !== tab.value) router.replace({ path: `/admin/${tab.value}`, query: route.query });
  url.read();
  loadTabData(tab.value);
});
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="flex items-center gap-3 mb-4">
      <h1 class="text-xl font-bold">{{ tr('Admin') }}</h1>
      <TableViewToggle class="ml-auto" />
    </div>

    <!-- mobile: current tab + dropdown; desktop: inline pill row -->
    <div class="mb-4">
      <button @click="mobileTabsOpen = !mobileTabsOpen"
              class="md:hidden w-full flex items-center justify-between gap-2 rounded-lg border border-slate-300 dark:border-slate-700 px-3 py-2 text-sm font-medium">
        {{ tr(tabDefs.find((t) => t[0] === tab)?.[1]) }}
        <svg viewBox="0 0 24 24" class="w-4 h-4 shrink-0 transition-transform" :class="{ 'rotate-180': mobileTabsOpen }"
             fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 9l6 6 6-6" /></svg>
      </button>
      <div :class="mobileTabsOpen ? 'flex' : 'hidden'"
           class="md:flex flex-col md:flex-row flex-wrap gap-1 mt-1 md:mt-0 pb-2 border-b border-slate-200 dark:border-slate-800 text-sm">
        <button v-for="t in tabDefs" :key="t[0]" @click="switchTab(t[0])"
                class="whitespace-nowrap text-left md:text-center rounded-lg px-3 py-1.5"
                :class="tab === t[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          {{ tr(t[1]) }}
        </button>
      </div>
    </div>

    <p v-if="err" role="alert" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ err }}</p>

    <!-- Dashboard: at-a-glance overview -->
    <section v-show="tab === 'dashboard'" class="space-y-5">
      <button @click="loadDashboard" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>

      <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
        <button type="button" @click="switchTab('users')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Users') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalUsers) }}</div>
          <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
            {{ tr('{t} teacher · {s} student · {a} admin', { t: dashboard.teacherCount, s: dashboard.studentCount, a: dashboard.adminCount }) }}
          </div>
        </button>

        <button type="button" @click="switchTab('boards')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Boards') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalBoards) }}</div>
        </button>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Problems') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalProblems + dashboard.totalBankProblems) }}</div>
          <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
            {{ tr('{a} on boards · {b} in bank', { a: fmt(dashboard.totalProblems), b: fmt(dashboard.totalBankProblems) }) }}
          </div>
        </div>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Submissions') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalSubmissions) }}</div>
          <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
            {{ tr('{n}% accepted', { n: dashboard.totalSubmissions ? Math.round(100 * dashboard.acceptedSubmissions / dashboard.totalSubmissions) : 0 }) }}
          </div>
        </div>

        <button type="button" @click="switchTab('review')" class="text-left border rounded-xl p-4"
                :class="dashboard.pendingAiReview
                  ? 'border-amber-300 dark:border-amber-500/40 bg-amber-50/50 dark:bg-amber-500/5 hover:border-amber-400'
                  : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Pending problem review') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.pendingAiReview) }}</div>
        </button>

        <button type="button" @click="switchTab('trash')" class="text-left border rounded-xl p-4"
                :class="dashboard.trashCount
                  ? 'border-rose-300 dark:border-rose-500/40 bg-rose-50/50 dark:bg-rose-500/5 hover:border-rose-400'
                  : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('In trash') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.trashCount) }}</div>
        </button>

        <button type="button" @click="switchTab('ai')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('AI calls today / this month') }}</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.aiToday.calls) }} / {{ fmt(dashboard.aiMonth.calls) }}</div>
          <div class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
            {{ tr('{a} / {b} tokens', { a: fmt(dashboard.aiToday.totalTokens), b: fmt(dashboard.aiMonth.totalTokens) }) }}
          </div>
        </button>
      </div>
      <p v-else-if="!dashboard" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('Loading…') }}</p>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h3 class="font-semibold text-sm">{{ tr('Engagement') }}</h3>
          <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="g in [['hour', 'Hourly'], ['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                    @click="setEngagementGranularity(g[0])" class="px-2.5 py-1"
                    :class="engagementGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ g[1] }}
            </button>
          </span>
        </div>
        <div v-if="hasData(engagementStats)" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="tr('Active users / {g}', { g: tr(engagementGranularity) })" :points="activeUserPoints()" />
          <MiniLineChart :title="tr('Submissions / {g}', { g: tr(engagementGranularity) })" :points="submissionPoints()" />
        </div>
        <p v-else-if="engagementStats" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No activity in this window yet.') }}</p>
      </div>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h3 class="font-semibold text-sm">{{ tr('AI usage') }}</h3>
          <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
            <button v-for="g in [['day', 'Daily'], ['week', 'Weekly']]" :key="g[0]"
                    @click="setAiGranularity(g[0])" class="px-2.5 py-1"
                    :class="aiGranularity === g[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
              {{ g[1] }}
            </button>
          </span>
        </div>
        <div v-if="hasData(aiEngagementStats)" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <MiniLineChart :title="tr('AI calls / {g}', { g: tr(aiGranularity) })" :points="aiCallPoints()" />
          <MiniLineChart :title="tr('AI tokens / {g}', { g: tr(aiGranularity) })" :points="aiTokenPoints()" />
        </div>
        <p v-else-if="aiEngagementStats" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No AI usage yet.') }}</p>
      </div>

      <div v-if="topicStats">
        <h2 class="font-semibold text-sm mb-1.5">{{ tr('Top topics by attempts') }}</h2>
        <TopicBarChart :items="topicBarItems()" />
      </div>

      <div v-if="dashboard">
        <div class="flex items-center gap-2 mb-1.5">
          <h2 class="font-semibold text-sm">{{ tr('Recent activity') }}</h2>
          <button @click="switchTab('audit')" class="row-action-btn">{{ tr('view all') }}</button>
        </div>
        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2">
          <div v-for="r in dashboard.recentActivity" :key="r.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="flex items-center justify-between gap-2 mb-1">
              <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span>
              <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</span>
            </div>
            <div class="text-xs">{{ r.actorEmail }}</div>
            <div class="text-[11px] text-slate-400 mt-0.5">{{ r.targetType }} · {{ r.targetLabel }}</div>
          </div>
          <p v-if="!dashboard.recentActivity.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No activity yet.') }}</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="r in dashboard.recentActivity" :key="r.id" class="border-b border-slate-100 dark:border-slate-800/60">
                <td class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</td>
                <td class="text-[11px]">{{ r.actorEmail }}</td>
                <td><span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span></td>
                <td class="text-[11px]">{{ r.targetType }} · {{ r.targetLabel }}</td>
              </tr>
              <tr v-if="!dashboard.recentActivity.length"><td colspan="4" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No activity yet.') }}</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <!-- AI usage -->
    <section v-show="tab === 'ai'" class="space-y-5">
      <!-- Kill-switch + default daily quotas -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4" v-if="aiSettings">
        <h2 class="font-semibold text-sm mb-2">{{ tr('Global AI settings') }}</h2>
        <label class="flex items-center gap-2 text-sm mb-2">
          <input type="checkbox" v-model="aiSettings.paused" />
          <span :class="aiSettings.paused ? 'text-rose-600 dark:text-rose-400 font-medium' : ''">
            {{ tr('Pause AI platform-wide') }} {{ aiSettings.paused ? tr('(paused — no AI feature works right now)') : '' }}
          </span>
        </label>
        <input v-model="aiSettings.pausedReason" :placeholder="tr('Reason (shown in the audit log only)')" :aria-label="tr('Reason (shown in the audit log only)')"
               class="w-full mb-3 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
        <div class="flex flex-wrap gap-4 mb-3 text-sm">
          <label class="flex items-center gap-2">{{ tr('Daily quota · students') }}
            <input v-model.number="aiSettings.dailyQuotaStudent" type="number" min="0"
                   class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label class="flex items-center gap-2">{{ tr('Daily quota · teachers') }}
            <input v-model.number="aiSettings.dailyQuotaTeacher" type="number" min="0"
                   class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
        </div>
        <p class="text-[11px] text-slate-500 dark:text-slate-400 mb-3">
          {{ tr('Requests per user per calendar day (UTC), across hints + problem generation combined. Takes effect on the next AI request platform-wide — no restart needed.') }}
        </p>
        <button @click="saveAiSettings" :disabled="aiSettingsSaving"
                class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ aiSettingsSaving ? tr('Saving…') : tr('Save') }}
        </button>
      </div>

      <!-- Which AI provider/credential is billed -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4" v-if="aiProvider">
        <h2 class="font-semibold text-sm mb-1">{{ tr('AI provider (platform default)') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
          {{ tr('Overrides appsettings.json\'s') }} <code>Ai:*</code> {{ tr('at runtime. An organization can set its own key/model in') }} <RouterLink to="/org-admin" class="underline">{{ tr('Organization') }}</RouterLink>
          {{ tr('→ AI settings, which takes priority over this for that organization only.') }}
        </p>
        <div class="space-y-2 text-sm max-w-md">
          <label class="flex flex-col gap-1">
            <span class="text-xs text-slate-500 dark:text-slate-400">
              {{ tr('API key') }} {{ aiProvider.hasApiKey ? tr('(saved: {p})', { p: aiProvider.apiKeyPreview }) : tr('(none saved — using appsettings.json, if any)') }}
            </span>
            <div class="flex gap-2">
              <input v-model="aiProviderForm.apiKey" type="password" :placeholder="tr('Leave blank to keep the saved key')" :aria-label="tr('Leave blank to keep the saved key')"
                     class="flex-1 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
              <button v-if="aiProvider.hasApiKey" @click="clearAiProviderKey" class="row-action-btn row-action-btn--danger shrink-0">{{ tr('Clear') }}</button>
            </div>
          </label>
          <label for="ai-base-url" class="block text-xs text-slate-500 dark:text-slate-400 mt-2">{{ tr('Base URL') }}</label>
          <input id="ai-base-url" v-model="aiProviderForm.baseUrl" :placeholder="tr('Base URL (blank = appsettings.json default)')" :aria-label="tr('Base URL (blank = appsettings.json default)')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <label for="ai-model" class="block text-xs text-slate-500 dark:text-slate-400 mt-2">{{ tr('Model') }}</label>
          <input id="ai-model" v-model="aiProviderForm.model" :placeholder="tr('Model (blank = appsettings.json default)')" :aria-label="tr('Model (blank = appsettings.json default)')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <label for="ai-generate-model" class="block text-xs text-slate-500 dark:text-slate-400 mt-2">{{ tr('Model for generating problems') }}</label>
          <input id="ai-generate-model" v-model="aiProviderForm.generateModel" :placeholder="tr('Generate-problem model (blank = same as above)')" :aria-label="tr('Generate-problem model (blank = same as above)')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
        </div>
        <p v-if="aiProviderMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mt-2">{{ aiProviderMsg }}</p>
        <button @click="saveAiProvider" :disabled="aiProviderSaving"
                class="mt-3 bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ aiProviderSaving ? tr('Saving…') : tr('Save') }}
        </button>
      </div>

      <!-- Are hints helping? -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4" v-if="hintEffect">
        <div class="flex items-center gap-2 mb-1">
          <h2 class="font-semibold text-sm">{{ tr('Are AI hints helping?') }}</h2>
          <select v-model.number="hintEffectDays" @change="loadHintEffect" :aria-label="tr('Time window')"
                  class="ml-auto border border-slate-400 dark:border-slate-500 rounded-lg px-2 py-1 text-xs dark:bg-slate-800">
            <option :value="7">{{ tr('Last 7 days') }}</option><option :value="30">{{ tr('Last 30 days') }}</option><option :value="90">{{ tr('Last 90 days') }}</option>
          </select>
        </div>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
          {{ tr('Per student and problem that asked for a hint (hints are logged from the release that added this, with no text). Pairs already solved before the first hint are left out. Students ask on the harder problems, so the “no hint” number is not a fair comparison — trust the speed numbers more.') }}
        </p>
        <p v-if="!hintEffect.pairs" class="text-sm text-slate-500">{{ tr('No hints logged in this period yet.') }}</p>
        <dl v-else class="grid grid-cols-2 md:grid-cols-3 gap-3 text-sm">
          <div><dt class="text-xs text-slate-500">{{ tr('Student–problem pairs') }}</dt><dd class="font-semibold">{{ hintEffect.pairs }} <span class="text-xs font-normal text-slate-400">({{ tr('{n} students', { n: hintEffect.students }) }})</span></dd></div>
          <div><dt class="text-xs text-slate-500">{{ tr('Solved after a hint') }}</dt><dd class="font-semibold">{{ pct(hintEffect.solvedAfterPct) }}</dd></div>
          <div><dt class="text-xs text-slate-500">{{ tr('Solved within 30 min') }}</dt><dd class="font-semibold">{{ pct(hintEffect.solvedWithin30MinPct) }}</dd></div>
          <div><dt class="text-xs text-slate-500">{{ tr('Median time to solve') }}</dt><dd class="font-semibold">{{ hintEffect.medianMinutesToSolve == null ? '–' : hintEffect.medianMinutesToSolve + ' min' }}</dd></div>
          <div><dt class="text-xs text-slate-500">{{ tr('Deep hints (level 3+), never solved') }}</dt><dd class="font-semibold">{{ hintEffect.deepStuck }}</dd></div>
          <div><dt class="text-xs text-slate-500">{{ tr('No hint, same problems: solved') }}</dt><dd class="font-semibold">{{ pct(hintEffect.baselineSolvedPct) }} <span class="text-xs font-normal text-slate-400">({{ hintEffect.baselinePairs }})</span></dd></div>
        </dl>
      </div>

      <!-- Tuning + backup providers -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4" v-if="aiExtras">
        <h2 class="font-semibold text-sm mb-1">{{ tr('AI tuning and backup providers') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
          {{ tr('Blank fields use appsettings.json\'s') }} <code>Ai:*</code>. Backups are tried in order when the provider above fails
          (timeout, 429, 5xx, 401/403, empty reply — not for a 400/422). A hint only moves to a backup before its first word is shown.
          An organization with its own key never falls back to these.
        </p>
        <div class="grid sm:grid-cols-2 gap-2 text-sm max-w-md mb-3">
          <label class="flex flex-col gap-1">
            <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Max tokens per hint (100–16000)') }}</span>
            <input v-model="aiExtrasForm.maxTokens" type="number" min="100" max="16000" :placeholder="tr('appsettings')" :aria-label="tr('appsettings')"
                   class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Reasoning effort') }}</span>
            <select v-model="aiExtrasForm.reasoningEffort"
                    class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" :aria-label="tr('appsettings / don\'t send')">
              <option value="">{{ tr('appsettings / don\'t send') }}</option>
              <option value="low">{{ tr('low') }}</option><option value="medium">{{ tr('medium') }}</option><option value="high">{{ tr('high') }}</option>
            </select>
          </label>
        </div>
        <label class="flex flex-col gap-1 text-sm max-w-md mb-3">
          <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Review model (AI check + report triage; blank = the generate-problem model)') }}</span>
          <input v-model="aiExtrasForm.reviewModel" :placeholder="tr('e.g. mistralai/mistral-large-4-1t-a52b')" :aria-label="tr('Model')"
                 class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
        </label>
        <label class="flex flex-col gap-1 text-sm max-w-md mb-3">
          <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Effort for problem generation + AI review (turns thinking on; blank = quiet and fast: low, no thinking)') }}</span>
          <select v-model="aiExtrasForm.jobEffort" :aria-label="tr('Effort for generation and review')"
                  class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5">
            <option value="">{{ tr('quiet default (low, no thinking)') }}</option>
            <option value="low">{{ tr('low') }}</option><option value="medium">{{ tr('medium') }}</option><option value="high">{{ tr('high') }}</option>
          </select>
        </label>
        <div v-for="(f, i) in aiExtrasForm.fallbacks" :key="f.id || 'new' + i" class="border border-slate-200 dark:border-slate-800 rounded-lg p-3 mb-2 max-w-md space-y-2 text-sm">
          <div class="flex items-center gap-2">
            <span class="text-xs font-semibold text-slate-500 dark:text-slate-400">{{ tr('Backup {n}', { n: i + 1 }) }}</span>
            <button @click="aiExtrasForm.fallbacks.splice(i, 1)" class="row-action-btn row-action-btn--danger ml-auto">{{ tr('Remove') }}</button>
          </div>
          <input v-model="f.model" :placeholder="tr('Model, e.g. moonshotai/kimi-k3.1-pro')" :aria-label="tr('Model')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="f.apiKey" type="password" :placeholder="f.keyPreview ? tr('API key (saved: {p}) — blank keeps it', { p: f.keyPreview }) : tr('API key (blank = same as the main provider)')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="f.baseUrl" :placeholder="tr('Base URL (blank = same as the main provider)')" :aria-label="tr('Base URL (blank = same as the main provider)')"
                 class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
        </div>
        <button v-if="aiExtrasForm.fallbacks.length < 5" @click="addAiFallback" class="row-action-btn row-action-btn--accent">{{ tr('+ Add backup provider') }}</button>
        <p v-if="aiExtrasMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mt-2">{{ aiExtrasMsg }}</p>
        <div>
          <button @click="saveAiExtras" :disabled="aiExtrasSaving"
                  class="mt-3 bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ aiExtrasSaving ? tr('Saving…') : tr('Save') }}
          </button>
        </div>
      </div>

      <!-- Per-user overrides / bans -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-2">{{ tr('Per-user overrides') }}</h2>
        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2 mb-3">
          <div v-for="row in aiOverrides" :key="row.userId" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="font-medium text-sm">{{ row.displayName }}</div>
            <div class="text-[11px] text-slate-400 mb-2">{{ row.email }}</div>
            <div class="flex items-center gap-3 flex-wrap text-xs">
              <label class="flex items-center gap-1.5">{{ tr('Quota') }}
                <input v-model.number="row.dailyQuotaOverride" type="number" min="0" :placeholder="tr('role default')" :aria-label="tr('role default')"
                       class="w-24 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-xs" />
              </label>
              <label class="flex items-center gap-1.5"><input type="checkbox" v-model="row.banned" /> {{ tr('Banned') }}</label>
            </div>
            <div class="mt-2 flex gap-3">
              <button @click="saveOverride(row)" class="row-action-btn row-action-btn--success">{{ tr('Save') }}</button>
              <button @click="clearOverride(row)" class="row-action-btn">{{ tr('Clear') }}</button>
            </div>
          </div>
          <p v-if="aiOverrides && !aiOverrides.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No overrides set.') }}</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto mb-3">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">{{ tr('User') }}</th><th class="font-normal pr-3">{{ tr('Daily quota') }}</th>
                <th class="font-normal pr-3">{{ tr('Banned') }}</th><th class="font-normal pr-3"></th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="row in aiOverrides" :key="row.userId" class="border-b border-slate-100 dark:border-slate-800/60">
                <td><div class="font-medium">{{ row.displayName }}</div><div class="text-[11px] text-slate-400">{{ row.email }}</div></td>
                <td>
                  <input v-model.number="row.dailyQuotaOverride" type="number" min="0" :placeholder="tr('role default')" :aria-label="tr('role default')"
                         class="w-24 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-xs" />
                </td>
                <td><input type="checkbox" v-model="row.banned" /></td>
                <td class="whitespace-nowrap">
                  <button @click="saveOverride(row)" class="row-action-btn row-action-btn--success mr-1">{{ tr('Save') }}</button>
                  <button @click="clearOverride(row)" class="row-action-btn">{{ tr('Clear') }}</button>
                </td>
              </tr>
              <tr v-if="aiOverrides && !aiOverrides.length"><td colspan="4" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No overrides set.') }}</td></tr>
            </tbody>
          </table>
        </div>
        <div class="flex flex-wrap items-end gap-2 text-sm">
          <label class="flex flex-col text-xs text-slate-500 dark:text-slate-400">{{ tr('User ID') }}
            <input v-model.number="newOverride.userId" type="number" :placeholder="tr('see Users tab')" :aria-label="tr('see Users tab')"
                   class="w-28 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm" />
          </label>
          <label class="flex flex-col text-xs text-slate-500 dark:text-slate-400">{{ tr('Daily quota') }}
            <input v-model.number="newOverride.quota" type="number" min="0" :placeholder="tr('role default')" :aria-label="tr('role default')"
                   class="w-28 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm" />
          </label>
          <label class="flex items-center gap-1.5"><input type="checkbox" v-model="newOverride.banned" /> {{ tr('Banned') }}</label>
          <button @click="addOverride" :disabled="!newOverride.userId"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ tr('Add / update') }}
          </button>
        </div>
      </div>

      <!-- Usage -->
      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">{{ tr('Usage') }}</h2>
          <button @click="loadAi" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>
          <button @click="downloadFile('/api/admin-ui/analytics/ai-usage.csv', 'ai-usage.csv')"
                  class="text-xs text-slate-500 dark:text-slate-400 ml-auto"><Icon name="download" /> {{ tr('export CSV') }}</button>
        </div>
        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2">
          <div class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
            <label for="ai-sort">{{ tr('Sort by') }}</label>
            <select id="ai-sort" :value="aiSort" @change="aiSort = $event.target.value; aiDir = firstDir(aiSort, false); aiPage = 1; loadAi()"
                    class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-1.5 py-1">
              <option v-for="[k, l] in AI_SORTS" :key="k" :value="k">{{ l }}</option>
            </select>
            <button type="button" @click="aiDir = aiDir === 'asc' ? 'desc' : 'asc'; aiPage = 1; loadAi()" class="row-action-btn"
                    :title="aiDir === 'asc' ? tr('Ascending') : tr('Descending')">{{ aiDir === 'asc' ? '▲' : '▼' }}</button>
          </div>
          <div v-for="r in aiRows" :key="r.userId" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="font-medium text-sm">{{ r.displayName }}</div>
            <div class="text-[11px] text-slate-400 mb-1.5">{{ r.email }}</div>
            <div class="text-xs grid grid-cols-3 gap-2 tabular-nums">
              <div><div class="text-[11px] text-slate-400">{{ tr('Today') }}</div>{{ fmt(r.today.calls) }} / {{ fmt(r.today.totalTokens) }}</div>
              <div><div class="text-[11px] text-slate-400">{{ tr('Month') }}</div>{{ fmt(r.month.calls) }} / {{ fmt(r.month.totalTokens) }}</div>
              <div><div class="text-[11px] text-slate-400">{{ tr('All time') }}</div><span class="font-medium">{{ fmt(r.allTime.calls) }} / {{ fmt(r.allTime.totalTokens) }}</span></div>
            </div>
          </div>
          <p v-if="aiRows && !aiRows.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No AI usage yet.') }}</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <SortTh class="py-1.5" field="user" label="User" :sort="aiSort" :dir="aiDir" @sort="sortAi" />
                <SortTh field="today" label="Today (calls / tokens)" :sort="aiSort" :dir="aiDir" @sort="sortAi" />
                <SortTh field="month" label="This month" :sort="aiSort" :dir="aiDir" @sort="sortAi" />
                <SortTh field="" label="All time" :sort="aiSort" :dir="aiDir" @sort="sortAi" />
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3 [&_td]:align-top">
              <tr v-for="r in aiRows" :key="r.userId" class="border-b border-slate-100 dark:border-slate-800/60">
                <td><div class="font-medium">{{ r.displayName }}</div><div class="text-[11px] text-slate-400">{{ r.email }}</div></td>
                <td class="tabular-nums">{{ fmt(r.today.calls) }} / {{ fmt(r.today.totalTokens) }}</td>
                <td class="tabular-nums">{{ fmt(r.month.calls) }} / {{ fmt(r.month.totalTokens) }}</td>
                <td class="tabular-nums font-medium">{{ fmt(r.allTime.calls) }} / {{ fmt(r.allTime.totalTokens) }}</td>
              </tr>
              <tr v-if="aiRows && !aiRows.length"><td colspan="4" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No AI usage yet.') }}</td></tr>
            </tbody>
          </table>
        </div>
        <div v-if="aiTotal" class="flex items-center gap-3 mt-3 text-sm">
          <span class="text-slate-500 dark:text-slate-400">
            {{ (aiPage - 1) * aiPageSize + 1 }}–{{ Math.min(aiPage * aiPageSize, aiTotal) }} of {{ aiTotal }}
          </span>
          <div class="ml-auto flex gap-2">
            <button @click="aiPrevPage" :disabled="aiPage === 1"
                    class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Prev') }}</button>
            <button @click="aiNextPage" :disabled="aiPage * aiPageSize >= aiTotal"
                    class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Next') }}</button>
          </div>
        </div>
      </div>
    </section>

    <!-- Users -->
    <section v-show="tab === 'users'">
      <!-- two-step verification policy -->
      <div v-if="mfaPolicy" class="mb-4 border border-slate-200 dark:border-slate-800 rounded-xl p-4 space-y-2">
        <div class="flex flex-col sm:flex-row sm:items-center gap-x-3 gap-y-1">
          <h2 class="font-semibold text-sm whitespace-nowrap"><Icon name="lock" /> {{ tr('Require two-step verification') }}</h2>
          <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Off by default. Accounts in a switched-on group are held on their Account page until they turn on an authenticator app or email code.') }}</span>
        </div>
        <div class="flex flex-col sm:flex-row sm:flex-wrap gap-x-6 gap-y-2.5 pt-1">
          <label v-for="g in MFA_GROUPS" :key="g.key" class="flex items-center gap-2 text-sm cursor-pointer">
            <input type="checkbox" :checked="mfaPolicy[g.key]" :disabled="mfaPolicyBusy" @click.prevent="toggleMfaPolicy(g)" class="accent-amber-500 w-4 h-4" />
            <span>{{ tr(g.label) }}</span>
            <span class="text-xs tabular-nums" :class="mfaPolicy[g.without] > 0 ? 'text-amber-700 dark:text-amber-400' : 'text-slate-500 dark:text-slate-400'"
                  :title="tr('{n} without two-step verification', { n: mfaPolicy[g.without] })">· {{ tr('{n} without', { n: mfaPolicy[g.without] }) }}</span>
          </label>
        </div>
      </div>
      <div v-if="userStats" class="flex flex-wrap gap-2 mb-3 text-xs">
        <div v-for="c in SECURITY_CHIPS" :key="c.key" class="inline-flex items-center rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
          <button @click="setUserFilter(c.key)" :aria-pressed="userFilter === c.key"
                  class="px-2.5 py-1 tabular-nums"
                  :class="userFilter === c.key ? 'bg-emerald-500 text-white' : 'hover:bg-slate-100 dark:hover:bg-slate-800'"
                  :title="tr('Show only users with: {label}', { label: tr(c.label) })">
            {{ tr(c.label) }} <b>{{ userStats[c.stat] }}</b><span class="text-slate-400" :class="{ 'text-white/80': userFilter === c.key }"> / {{ userStats.users }}</span>
          </button>
          <button v-if="c.miss" @click="setUserFilter(c.miss)" :aria-pressed="userFilter === c.miss"
                  class="px-2 py-1 tabular-nums border-l border-slate-200 dark:border-slate-800"
                  :class="userFilter === c.miss ? 'bg-amber-500 text-slate-900' : 'text-amber-700 dark:text-amber-400 hover:bg-slate-100 dark:hover:bg-slate-800'"
                  :title="tr('Show only users {label}', { label: tr(c.missLabel) })">{{ userStats.users - userStats[c.stat] }} {{ tr(c.missLabel) }}</button>
        </div>
      </div>
      <div class="flex gap-2 mb-3 flex-wrap items-center">
        <input v-model="userQ" @keyup.enter="searchUsers" :placeholder="tr('Search name or email…')" :aria-label="tr('Search name or email…')"
               class="flex-1 min-w-[10rem] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="searchUsers" class="self-stretch text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">{{ tr('Search') }}</button>
        <button @click="recalculateProgress" :disabled="recalcBusy"
                class="row-action-btn row-action-btn--accent shrink-0 disabled:opacity-50"
                :title="tr('Rebuild everyone\'s streak / longest streak / best-day-solved from raw submission history')">
          {{ recalcBusy ? tr('Recalculating…') : tr('↻ Recalculate progress') }}
        </button>
      </div>
      <div v-if="selectedUsers.size" class="flex items-center gap-2 mb-2 text-sm">
        <span>{{ tr('{n} selected', { n: selectedUsers.size }) }}</span>
        <button @click="deleteSelectedUsers" class="bg-rose-600 hover:bg-rose-700 text-white rounded-lg px-3 py-1 text-xs font-medium">
          {{ tr('Delete selected') }}
        </button>
      </div>
      <p v-if="userDeleteResultMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mb-2">{{ userDeleteResultMsg }}</p>
      <p v-if="recalcResultMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mb-2">{{ recalcResultMsg }}</p>

      <!-- mobile: cards -->
      <div v-if="tableView === 'card'" class="space-y-2">
        <div class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
          <label for="users-sort">{{ tr('Sort by') }}</label>
          <select id="users-sort" :value="usersSort" @change="usersSort = $event.target.value; usersDir = firstDir(usersSort, true); usersPage = 1; loadUsers()"
                  class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-1.5 py-1">
            <option v-for="[k, l] in USER_SORTS" :key="k" :value="k">{{ l }}</option>
          </select>
          <button type="button" @click="usersDir = usersDir === 'asc' ? 'desc' : 'asc'; usersPage = 1; loadUsers()" class="row-action-btn"
                  :title="usersDir === 'asc' ? tr('Ascending') : tr('Descending')">{{ usersDir === 'asc' ? '▲' : '▼' }}</button>
        </div>
        <label v-if="users?.length" class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400 px-1">
          <input type="checkbox" :checked="selectedUsers.size === users.filter((u) => u.id !== auth.user?.id).length"
                 @change="selectAllUsers($event.target.checked)" /> {{ tr('Select all') }}
        </label>
        <div v-for="u in users" :key="u.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-start gap-2">
            <input v-if="u.id !== auth.user?.id" type="checkbox" :checked="selectedUsers.has(u.id)" @change="toggleUserSelect(u.id)" class="mt-1 shrink-0" />
            <div class="min-w-0 flex-1">
              <div class="font-medium text-sm truncate">{{ u.displayName }} <span class="text-slate-400 font-normal">#{{ u.id }}</span></div>
              <div class="text-[11px] text-slate-400 truncate">{{ u.email }}</div>
            </div>
            <div v-if="u.id !== auth.user?.id" class="shrink-0 -mt-1 -mr-1">
              <AdminUserActions :user="u" @verify="markVerified(u)" @reset-link="issueResetLink(u)" @reset-mfa="resetMfa(u)"
                                @grant-admin="grantAdmin(u)" @revoke-admin="revokeAdmin(u)" @delete="deleteUser(u)" />
            </div>
          </div>
          <div class="flex items-center gap-2 flex-wrap mt-2">
            <select v-if="u.id !== auth.user?.id" :value="u.role" @change="changeRole(u, $event.target.value)"
                    class="text-[11px] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-1 py-0.5">
              <option value="Student">{{ tr('Student') }}</option>
              <option value="Teacher">{{ tr('Teacher') }}</option>
            </select>
            <span v-else class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300">{{ u.role }}</span>
            <span v-if="u.isAdmin" class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300">{{ tr('admin') }}</span>
            <span v-if="!u.emailVerified" class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300" :title="tr('Email not verified')"><Icon name="message" /> {{ tr('unverified') }}</span>
            <span v-if="u.mfaEnabled" class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300" :title="tr('Two-step verification is on')"><Icon name="lock" /> 2FA</span>
            <span v-if="u.hasPasskey" class="inline-block text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300" :title="tr('Has a passkey for passwordless sign-in')"><Icon name="key" /> {{ tr('passkey') }}</span>
            <span class="text-[11px] text-slate-400 tabular-nums">{{ tr('{xp} XP · {b} boards · {s} subs', { xp: fmt(u.xp), b: u.ownedBoards, s: u.submissions }) }}</span>
          </div>
          <div class="text-[11px] text-slate-400 mt-1">{{ tr('Joined {when}', { when: new Date(u.createdAt).toLocaleDateString() }) }}</div>
        </div>
        <p v-if="users && !users.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No users.') }}</p>
      </div>

      <!-- desktop: table -->
      <div v-if="tableView === 'table'" class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">
                <input type="checkbox" :checked="!!users?.length && selectedUsers.size === users.filter((u) => u.id !== auth.user?.id).length"
                       @change="selectAllUsers($event.target.checked)" />
              </th>
              <SortTh class="py-1.5" field="" label="#" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <SortTh field="name" label="Name / email" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <SortTh field="role" label="Role" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <SortTh field="xp" label="XP" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <SortTh field="boards" label="Boards" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <SortTh field="subs" label="Subs" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <SortTh field="joined" label="Joined" :sort="usersSort" :dir="usersDir" @sort="sortUsers" />
              <th class="font-normal pr-3"></th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="u in users" :key="u.id" class="border-b border-slate-100 dark:border-slate-800/60">
              <td><input v-if="u.id !== auth.user?.id" type="checkbox" :checked="selectedUsers.has(u.id)" @change="toggleUserSelect(u.id)" /></td>
              <td class="text-slate-400">{{ u.id }}</td>
              <td><div class="font-medium">{{ u.displayName }}</div><div class="text-[11px] text-slate-400">{{ u.email }}</div></td>
              <td>
                <select v-if="u.id !== auth.user?.id" :value="u.role" @change="changeRole(u, $event.target.value)"
                        class="text-[11px] border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-1 py-0.5">
                  <option value="Student">{{ tr('Student') }}</option>
                  <option value="Teacher">{{ tr('Teacher') }}</option>
                </select>
                <span v-else class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300">{{ u.role }}</span>
                <span v-if="u.isAdmin" class="ml-1 text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300">{{ tr('admin') }}</span>
                <span v-if="!u.emailVerified" class="ml-1 text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300" :title="tr('Email not verified')"><Icon name="message" /> {{ tr('unverified') }}</span>
                <span v-if="u.mfaEnabled" class="ml-1 text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300" :title="tr('Two-step verification is on')"><Icon name="lock" /> 2FA</span>
                <span v-if="u.hasPasskey" class="ml-1 inline-block text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300" :title="tr('Has a passkey for passwordless sign-in')"><Icon name="key" /> {{ tr('passkey') }}</span>
              </td>
              <td class="tabular-nums">{{ fmt(u.xp) }}</td>
              <td class="tabular-nums">{{ u.ownedBoards }}</td>
              <td class="tabular-nums">{{ u.submissions }}</td>
              <td class="text-[11px] text-slate-400">{{ new Date(u.createdAt).toLocaleDateString() }}</td>
              <td class="whitespace-nowrap text-right">
                <AdminUserActions v-if="u.id !== auth.user?.id" :user="u" @verify="markVerified(u)" @reset-link="issueResetLink(u)"
                                  @reset-mfa="resetMfa(u)" @grant-admin="grantAdmin(u)" @revoke-admin="revokeAdmin(u)" @delete="deleteUser(u)" />
              </td>
            </tr>
            <tr v-if="users && !users.length"><td colspan="9" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No users.') }}</td></tr>
          </tbody>
        </table>
      </div>
      <div v-if="usersTotal" class="flex items-center gap-3 mt-3 text-sm">
        <span class="text-slate-500 dark:text-slate-400">
          {{ (usersPage - 1) * usersPageSize + 1 }}–{{ Math.min(usersPage * usersPageSize, usersTotal) }} of {{ usersTotal }}
        </span>
        <div class="ml-auto flex gap-2">
          <button @click="usersPrevPage" :disabled="usersPage === 1"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Prev') }}</button>
          <button @click="usersNextPage" :disabled="usersPage * usersPageSize >= usersTotal"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Next') }}</button>
        </div>
      </div>

      <div class="mt-5 border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">{{ tr('Bulk import (CSV)') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-2">
          {{ tr('Header row required — at least') }} <code>email</code> and <code>name</code> (or
          <code>displayname</code>); optional <code>role</code> and <code>password</code>
          {{ tr('columns. An email that already exists is left alone (just optionally added to the board below). A missing password is auto-generated and shown once — copy it down.') }}
        </p>
        <div class="flex flex-wrap gap-2 mb-2 text-sm">
          <select v-model="importDefaultRole" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm">
            <option value="Student">{{ tr('Default role: Student') }}</option>
            <option value="Teacher">{{ tr('Default role: Teacher') }}</option>
          </select>
          <select v-model="importBoardSlug" class="flex-1 min-w-48 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" :aria-label="tr('Don\'t assign to a board')">
            <option value="">{{ tr('Don\'t assign to a board') }}</option>
            <option v-for="b in boards" :key="b.slug" :value="b.slug">{{ tr('Add to: {title}', { title: b.title }) }}</option>
          </select>
        </div>
        <input type="file" accept="text/csv,.csv" :aria-label="tr('CSV file of users')" @change="importUsersCsv" :disabled="importingUsers" class="text-sm" />
        <p v-if="importingUsers" class="text-xs text-slate-400 mt-2">{{ tr('Importing…') }}</p>
        <div v-if="userImportResult" class="mt-3 text-sm">
          <p class="text-emerald-600 dark:text-emerald-400">
            {{ tr('Created {a} · Already existed {b} · Errors {c}', { a: userImportResult.created, b: userImportResult.existing, c: userImportResult.errors }) }}
          </p>
          <div class="overflow-x-auto mt-2">
            <table class="w-full text-xs">
              <thead>
                <tr class="text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                  <th class="font-normal py-1 pr-3">{{ tr('Email') }}</th><th class="font-normal pr-3">{{ tr('Name') }}</th>
                  <th class="font-normal pr-3">{{ tr('Password') }}</th><th class="font-normal pr-3">{{ tr('Note') }}</th>
                </tr>
              </thead>
              <tbody class="[&_td]:py-1 [&_td]:pr-3">
                <tr v-for="(row, i) in userImportResult.rows" :key="i" class="border-b border-slate-100 dark:border-slate-800/60">
                  <td>{{ row.email }}</td>
                  <td>{{ row.displayName }}</td>
                  <td class="font-mono">{{ row.generatedPassword || '—' }}</td>
                  <td :class="row.error ? 'text-red-600 dark:text-red-400' : 'text-slate-400'">
                    {{ row.error || (row.addedToBoard ? tr('added to board') : (row.created ? tr('created') : tr('already existed'))) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </section>

    <!-- Boards (browse all, across every teacher) -->
    <section v-show="tab === 'boards'">
      <div class="flex flex-wrap gap-2 mb-3 items-center">
        <input v-model="boardQ" @keyup.enter="loadBoards" :placeholder="tr('Search title or slug…')" :aria-label="tr('Search title or slug…')"
               class="flex-1 min-w-40 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="loadBoards" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-2">{{ tr('Search') }}</button>
        <span class="text-slate-300 dark:text-slate-700">|</span>
        <label class="text-xs text-slate-500 dark:text-slate-400 flex items-center gap-1">{{ tr('Select boards created before') }}
          <input type="date" v-model="archiveBeforeDate" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm" />
        </label>
        <button @click="selectBoardsBeforeDate" class="row-action-btn">{{ tr('select') }}</button>
      </div>
      <div v-if="selectedBoards.size" class="flex items-center gap-2 mb-2 text-sm">
        <span>{{ tr('{n} selected', { n: selectedBoards.size }) }}</span>
        <button @click="bulkAdding = true" class="bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-3 py-1 text-xs font-medium">
          <Icon name="plus" /> {{ tr('Add groups / problems') }}
        </button>
        <button @click="archiveSelectedBoards" class="bg-rose-600 hover:bg-rose-700 text-white rounded-lg px-3 py-1 text-xs font-medium">
          <Icon name="archive" /> {{ tr('Archive selected') }}
        </button>
      </div>
      <BulkAddPanel v-if="bulkAdding" :slugs="[...selectedBoards]" bank-url="/api/admin-ui/bank" add-url="/api/admin-ui/boards/bulk-add"
                    @done="loadBoards" @close="bulkAdding = false" />
      <p v-if="archiveResultMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mb-2">{{ archiveResultMsg }}</p>

      <!-- mobile: cards -->
      <div v-if="tableView === 'card'" class="space-y-2">
        <div v-for="b in boards" :key="b.slug" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-start gap-2">
            <input type="checkbox" :checked="selectedBoards.has(b.slug)" @change="toggleBoardSelect(b.slug)" class="mt-1 shrink-0" />
            <div class="min-w-0 flex-1">
              <RouterLink :to="`/boards/${b.slug}`" class="font-medium text-sm hover:underline">{{ b.title }}</RouterLink>
              <div class="text-[11px] text-slate-400 font-mono">{{ b.slug }}</div>
            </div>
            <button @click="deleteBoard(b)" class="row-action-btn row-action-btn--danger shrink-0">{{ tr('Delete') }}</button>
          </div>
          <div class="text-[11px] text-slate-400 mt-1.5">{{ b.ownerName }} ({{ b.ownerEmail }})</div>
          <div class="text-[11px] text-slate-400 mt-0.5">
            {{ tr('{a} student(s) · {b} problem(s) · created {when}', { a: b.memberCount, b: b.problemCount, when: new Date(b.createdAt).toLocaleDateString() }) }}
          </div>
        </div>
        <p v-if="boards && !boards.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No boards.') }}</p>
      </div>

      <!-- desktop: table -->
      <div v-if="tableView === 'table'" class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">
                <input type="checkbox" :checked="!!boards?.length && selectedBoards.size === boards.length"
                       @change="selectAllBoards($event.target.checked)" />
              </th>
              <th class="font-normal pr-3">{{ tr('Title') }}</th><th class="font-normal pr-3">{{ tr('Owner') }}</th>
              <th class="font-normal pr-3">{{ tr('Students') }}</th><th class="font-normal pr-3">{{ tr('Problems') }}</th>
              <th class="font-normal pr-3">{{ tr('Created') }}</th><th class="font-normal pr-3"></th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="b in boards" :key="b.slug" class="border-b border-slate-100 dark:border-slate-800/60">
              <td><input type="checkbox" :checked="selectedBoards.has(b.slug)" @change="toggleBoardSelect(b.slug)" /></td>
              <td><RouterLink :to="`/boards/${b.slug}`" class="font-medium hover:underline">{{ b.title }}</RouterLink>
                <div class="text-[11px] text-slate-400 font-mono">{{ b.slug }}</div></td>
              <td><div class="font-medium">{{ b.ownerName }}</div><div class="text-[11px] text-slate-400">{{ b.ownerEmail }}</div></td>
              <td class="tabular-nums">{{ b.memberCount }}</td>
              <td class="tabular-nums">{{ b.problemCount }}</td>
              <td class="text-[11px] text-slate-400">{{ new Date(b.createdAt).toLocaleDateString() }}</td>
              <td><button @click="deleteBoard(b)" class="row-action-btn row-action-btn--danger">{{ tr('Delete') }}</button></td>
            </tr>
            <tr v-if="boards && !boards.length"><td colspan="7" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No boards.') }}</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Problems -->
    <section v-show="tab === 'problems'" class="space-y-4">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">{{ tr('Export') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-2">
          {{ tr('Downloads bank problems (all owners) with their test cases. The ZIP has one JSON file per batch of problems plus an index.csv; each file can be imported on its own.') }}
        </p>
        <div class="flex flex-wrap gap-2 mb-2 text-sm">
          <select v-model="exportOpts.level" :aria-label="tr('Level')" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5">
            <option value="">{{ tr('All levels') }}</option><option value="Easy">{{ tr('Easy') }}</option><option value="Medium">{{ tr('Medium') }}</option><option value="Hard">{{ tr('Hard') }}</option>
          </select>
          <select v-model="exportOpts.visibility" :aria-label="tr('Visibility')" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5">
            <option value="">{{ tr('Public and private') }}</option><option value="public">{{ tr('Public only') }}</option><option value="private">{{ tr('Private only') }}</option>
          </select>
          <input v-model="exportOpts.tag" :placeholder="tr('Tag')" :aria-label="tr('Tag')" class="w-28 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5" />
          <input v-model="exportOpts.owner" :placeholder="tr('Owner email')" :aria-label="tr('Owner email')" class="w-52 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5" />
          <label class="flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400">{{ tr('Updated since') }}
            <input v-model="exportOpts.since" type="date" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm text-slate-900 dark:text-slate-100" />
          </label>
        </div>
        <div class="flex flex-wrap items-center gap-3 text-sm">
          <select v-model="exportOpts.format" :aria-label="tr('Format')" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1.5">
            <option value="zip">{{ tr('ZIP (one file per batch)') }}</option><option value="json">{{ tr('One JSON file') }}</option>
          </select>
          <label v-if="exportOpts.format === 'zip'" class="flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400">{{ tr('Problems per file') }}
            <select v-model.number="exportOpts.batch" class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm text-slate-900 dark:text-slate-100">
              <option v-for="n in [25, 50, 100, 250, 500]" :key="n" :value="n">{{ n }}</option>
            </select>
          </label>
          <button @click="exportProblems" :disabled="exporting"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            {{ exporting ? tr('Preparing…') : tr('Download') }}
          </button>
        </div>
      </div>

      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">{{ tr('Import') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-2">
          {{ tr('Upserts by slug when the file has one, else by (owner email, title). Owner resolved by email, falling back to the first teacher. A .json file or the exported .zip.') }}
        </p>
        <label class="flex items-center gap-2 text-xs mb-2">
          <input type="checkbox" v-model="replaceExisting" /> {{ tr('Replace existing (overwrite a problem with the same slug, or owner + title)') }}
        </label>
        <input type="file" accept="application/json,.json,application/zip,.zip" :aria-label="tr('problems.json or ZIP file')" @change="importProblems" :disabled="importing"
               class="text-sm" />
        <p v-if="importing" class="text-xs text-slate-400 mt-2">{{ tr('Importing…') }}</p>
        <div v-if="importResult" class="mt-3 text-sm">
          <p class="text-emerald-600 dark:text-emerald-400">
            {{ tr('Created {a} · Updated {b} · Skipped {c}', { a: importResult.created, b: importResult.updated, c: importResult.skipped }) }}
          </p>
          <ul v-if="importResult.errors?.length" class="mt-1 text-xs text-red-600 dark:text-red-400 list-disc pl-5">
            <li v-for="(e, i) in importResult.errors" :key="i">{{ e }}</li>
          </ul>
        </div>
      </div>
    </section>

    <!-- Submissions: every submission platform-wide — board (not just ones the admin owns/joined) AND practice -->
    <section v-show="tab === 'submissions'">
      <div class="flex flex-wrap gap-2 mb-3">
        <input v-model="submissionQ" @keyup.enter="searchSubmissions" :placeholder="tr('Search student, email, problem, or board…')" :aria-label="tr('Search student, email, problem, or board…')"
               class="flex-1 min-w-0 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <select v-model="submissionSource" @change="searchSubmissions"
                class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm" :aria-label="tr('Board + Practice')">
          <option value="">{{ tr('Board + Practice') }}</option>
          <option value="board">{{ tr('Board only') }}</option>
          <option value="practice">{{ tr('Practice only') }}</option>
        </select>
        <select v-model="submissionVerdict" @change="searchSubmissions"
                class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm" :aria-label="tr('Any verdict')">
          <option value="">{{ tr('Any verdict') }}</option>
          <option value="Accepted">{{ tr('Accepted') }}</option>
          <option value="WrongAnswer">{{ tr('Wrong answer') }}</option>
          <option value="TimeLimit">{{ tr('Time limit') }}</option>
          <option value="MemoryLimit">{{ tr('Memory limit') }}</option>
          <option value="RuntimeError">{{ tr('Runtime error') }}</option>
          <option value="CompileError">{{ tr('Compile error') }}</option>
        </select>
        <button @click="searchSubmissions" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">{{ tr('Search') }}</button>
        <button @click="loadSubmissions" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>
      </div>

      <!-- mobile: cards -->
      <div v-if="tableView === 'card'" class="space-y-2">
        <button v-for="s in submissionRows" :key="`${s.source}-${s.id}`" @click="openSubmission(s)"
                class="w-full text-left border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-center justify-between gap-2 mb-1">
            <VerdictBadge :verdict="s.verdict" small />
            <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(s.createdAt) }}</span>
          </div>
          <div class="text-sm font-medium">
            <RouterLink v-if="problemLink(s)" :to="problemLink(s)" @click.stop
                        class="hover:underline hover:text-amber-600 dark:hover:text-amber-400">{{ s.problemTitle }}</RouterLink>
            <template v-else>{{ s.problemTitle }}</template>
          </div>
          <div class="text-[11px] text-slate-400 mt-0.5">
            {{ s.userDisplayName }} ({{ s.userEmail }}) · {{ s.source === 'Board' ? s.boardTitle : tr('Practice') }}
          </div>
        </button>
        <p v-if="submissionRows && !submissionRows.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No submissions.') }}</p>
      </div>

      <!-- desktop: table -->
      <div v-if="tableView === 'table'" class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">{{ tr('When') }}</th><th class="font-normal pr-3">{{ tr('Student') }}</th>
              <th class="font-normal pr-3">{{ tr('Problem') }}</th><th class="font-normal pr-3">{{ tr('Board / Source') }}</th>
              <th class="font-normal pr-3">{{ tr('Verdict') }}</th><th class="font-normal pr-3">{{ tr('Score') }}</th>
              <th class="font-normal pr-3">{{ tr('Runtime') }}</th><th class="font-normal pr-3">{{ tr('Lang') }}</th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="s in submissionRows" :key="`${s.source}-${s.id}`" @click="openSubmission(s)"
                class="border-b border-slate-100 dark:border-slate-800/60 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40">
              <td class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(s.createdAt) }}</td>
              <td><div class="font-medium">{{ s.userDisplayName }}</div><div class="text-[11px] text-slate-400">{{ s.userEmail }}</div></td>
              <td>
                <RouterLink v-if="problemLink(s)" :to="problemLink(s)" @click.stop
                            class="hover:underline hover:text-amber-600 dark:hover:text-amber-400">{{ s.problemTitle }}</RouterLink>
                <template v-else>{{ s.problemTitle }}</template>
              </td>
              <td class="text-[11px] text-slate-400">{{ s.source === 'Board' ? s.boardTitle : tr('Practice') }}</td>
              <td><VerdictBadge :verdict="s.verdict" small /></td>
              <td class="tabular-nums">{{ Math.round(s.score * 100) }}%</td>
              <td class="text-[11px] text-slate-400 tabular-nums">{{ s.runtimeMs }}ms</td>
              <td class="text-[11px] text-slate-400">{{ s.language || '—' }}</td>
            </tr>
            <tr v-if="submissionRows && !submissionRows.length"><td colspan="8" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No submissions.') }}</td></tr>
          </tbody>
        </table>
      </div>
      <div v-if="submissionsTotal" class="flex items-center gap-3 mt-3 text-sm">
        <span class="text-slate-500 dark:text-slate-400">
          {{ (submissionsPage - 1) * submissionsPageSize + 1 }}–{{ Math.min(submissionsPage * submissionsPageSize, submissionsTotal) }} of {{ submissionsTotal }}
        </span>
        <div class="ml-auto flex gap-2">
          <button @click="submissionsPrevPage" :disabled="submissionsPage === 1"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Prev') }}</button>
          <button @click="submissionsNextPage" :disabled="submissionsPage * submissionsPageSize >= submissionsTotal"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Next') }}</button>
        </div>
      </div>

      <SubmissionView v-if="viewSubmission" :submission-id="viewSubmission.id" :source="viewSubmission.source"
                      :author-name="viewSubmission.authorName" @close="viewSubmission = null" />
    </section>

    <!-- Plagiarism -->
    <section v-show="tab === 'plagiarism'">
      <div class="flex items-center gap-2 mb-3">
        <select v-model="plagiarismBoardId" @change="pickPlagiarismBoard"
                class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
          <option :value="null">{{ tr('Pick a board…') }}</option>
          <option v-for="b in boards" :key="b.id" :value="b.id">{{ b.title }}</option>
        </select>
        <select v-if="plagiarismBoardId" v-model="plagiarismProblemId" @change="loadPlagiarism"
                class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm max-w-xs">
          <option :value="null">{{ tr('All problems') }}</option>
          <option v-for="p in plagiarismProblems" :key="p.id" :value="p.id">{{ p.title }}</option>
        </select>
      </div>
      <PlagiarismTable :result="plagiarismPairs" :loading="plagiarismLoading" :idle="!plagiarismBoardId" @view="plagiarismViewSubmission = $event"
                       @compare="plagiarismCompareData = $event" />
      <SubmissionView v-if="plagiarismViewSubmission" :submission-id="plagiarismViewSubmission.id"
                      :author-name="plagiarismViewSubmission.authorName" @close="plagiarismViewSubmission = null" />
      <SubmissionDiffView v-if="plagiarismCompareData" :submission-a-id="plagiarismCompareData.submissionAId"
                           :submission-b-id="plagiarismCompareData.submissionBId" :label-a="plagiarismCompareData.userAName"
                           :label-b="plagiarismCompareData.userBName" @close="plagiarismCompareData = null" />
    </section>

    <!-- Problem review: AI-generated and student-submitted bank problems held back until approved -->
    <section v-show="tab === 'review'">
      <div class="flex items-center gap-2 mb-3">
        <button @click="loadAiReview" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>
        <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('{n} waiting', { n: aiReview?.length ?? 0 }) }}</span>
      </div>
      <article v-for="row in aiReview" :key="row.slug" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 mb-3">
        <header class="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div class="min-w-0">
            <div class="flex items-center gap-2 flex-wrap">
              <h3 class="font-semibold text-sm break-words">{{ row.title }}</h3>
              <LevelBadge :level="row.level" />
              <RouterLink v-if="row.revisionOfSlug" :to="`/practice/${row.revisionOfSlug}`" class="text-[11px] px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300 hover:underline"
                          :title="tr('Approving replaces the live problem with this version')">{{ tr('Revision of “{p0}”', { p0: row.revisionOfTitle }) }}</RouterLink>
            </div>
            <p class="text-xs text-slate-500 dark:text-slate-400 mt-0.5 break-words">
              by <span class="font-medium text-slate-700 dark:text-slate-200">{{ row.ownerName }}</span>
              <span class="text-slate-500 dark:text-slate-400"> ({{ row.ownerEmail }})</span>
              · created {{ when(row.createdAt) }}
            </p>
          </div>
          <div class="flex items-center gap-2 flex-wrap lg:justify-end lg:shrink-0">
            <button @click="expandedReview = expandedReview === row.slug ? null : row.slug" class="row-action-btn">
              {{ expandedReview === row.slug ? tr('hide statement & tests') : tr('view statement & tests') }}
            </button>
            <button @click="copyReviewJson(row)" class="row-action-btn" :title="tr('Copy this problem as JSON (same format as Problems export/import)')">
              {{ reviewJsonCopied === row.slug ? tr('✓ copied') : tr('⧉ copy JSON') }}
            </button>
            <button @click="downloadReviewJson(row)" class="row-action-btn" :title="tr('Download this problem as a .json file')"><Icon name="download" /> JSON</button>
            <button @click="deleteAiReview(row)" class="row-action-btn row-action-btn--danger" :title="tr('Move this problem to Trash')"><span><Icon name="trash" /></span><span>{{ tr('Delete') }}</span></button>
            <button @click="approveAiReview(row)" class="text-xs bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg px-3 py-1.5 font-medium">
              {{ tr('✓ Approve & share') }}
            </button>
          </div>
        </header>

        <div class="mt-2.5 flex flex-wrap items-center gap-1.5 text-[11px]">
          <span class="rounded px-2 py-0.5 bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300">
            {{ row.allowedLanguages ? row.allowedLanguages.split(',').map((l) => (l === 'cpp' ? 'C++' : l.toUpperCase())).join(' · ') : tr('Any language') }}
          </span>
          <span v-for="tag in (row.tags || '').split(',').filter(Boolean)" :key="tag"
                class="rounded px-2 py-0.5 bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300">#{{ tag.trim() }}</span>
          <span v-if="!row.tags" class="text-slate-500 dark:text-slate-400">{{ tr('no tags') }}</span>
          <span class="text-slate-300 dark:text-slate-600">|</span>
          <span class="rounded px-2 py-0.5 bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300">{{ tr('{n} tests', { n: row.tests.length }) }}</span>
          <span class="rounded px-2 py-0.5 bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300"><Icon name="eye" /> {{ row.tests.filter((t) => t.isSample).length }} visible</span>
          <span class="rounded px-2 py-0.5 bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300"><Icon name="lock" /> {{ row.tests.filter((t) => !t.isSample).length }} hidden</span>
          <span v-if="!row.tests.some((t) => !t.isSample)" class="rounded px-2 py-0.5 bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-300 font-medium">
            <Icon name="alert" /> {{ tr('no hidden test — a solution could just print the samples') }}
          </span>
          <span v-if="row.inputFileName" class="rounded px-2 py-0.5 bg-sky-50 dark:bg-sky-500/10 text-sky-700 dark:text-sky-300"><Icon name="file" /> {{ tr('reads') }} <span class="font-mono">{{ row.inputFileName }}</span>, not stdin</span>
          <span v-if="row.bannedHeaders" class="rounded px-2 py-0.5 bg-amber-50 dark:bg-amber-500/10 text-amber-700 dark:text-amber-300"><Icon name="ban" /> {{ tr('headers:') }} <span class="font-mono">{{ row.bannedHeaders }}</span></span>
          <span v-if="row.bannedSymbols" class="rounded px-2 py-0.5 bg-amber-50 dark:bg-amber-500/10 text-amber-700 dark:text-amber-300"><Icon name="ban" /> {{ tr('functions:') }} <span class="font-mono">{{ row.bannedSymbols }}</span></span>
        </div>

        <div v-if="row.id" class="mt-3 pt-3 border-t border-slate-100 dark:border-slate-800 flex flex-wrap items-start gap-x-4 gap-y-2">
          <AiCheckPanel kind="bank" :id="row.id" class="w-full" />
          <SimilarProblems :url="`/api/admin-ui/bank/${row.id}/similar`" class="w-full" />
        </div>

        <div v-if="expandedReview === row.slug" class="mt-3 space-y-3">
          <div class="bg-slate-50 dark:bg-slate-800/40 rounded-lg p-3 max-h-64 overflow-auto">
            <MarkdownBlock :text="row.statementMarkdown" />
          </div>
          <div class="grid sm:grid-cols-2 gap-2">
            <div v-for="(t, i) in row.tests" :key="i" class="text-xs border border-slate-100 dark:border-slate-800 rounded-lg p-2">
              <div class="text-slate-400 mb-1 flex items-center gap-1.5 flex-wrap">#{{ i + 1 }}
                <span v-if="t.isSample" class="px-1.5 py-0.5 rounded bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300"><Icon name="eye" /> {{ tr('visible to students (sample)') }}</span>
                <span v-else class="px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300"><Icon name="lock" /> {{ tr('hidden from students (graded)') }}</span>
              </div>
              <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto">stdin: {{ t.stdin }}</pre>
              <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto mt-1">expected: {{ t.expectedStdout }}</pre>
            </div>
          </div>
          <div class="flex items-center gap-2 flex-wrap">
            <input v-model="rejectReasonDraft[row.slug]" :placeholder="tr('Reason (optional, goes in the audit log)')" :aria-label="tr('Reason (optional, goes in the audit log)')"
                   class="flex-1 min-w-48 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
            <button @click="rejectAiReview(row)" class="text-xs bg-rose-600 hover:bg-rose-700 text-white rounded-lg px-3 py-1.5 font-medium">
              {{ tr('✕ Reject (stays private)') }}
            </button>
          </div>
        </div>
      </article>
      <p v-if="aiReview && !aiReview.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('Nothing waiting for review.') }}</p>
    </section>

    <!-- Reports: analytics export + system status -->
    <section v-show="tab === 'reports'" class="space-y-4">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">{{ tr('Analytics export') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
          {{ tr('CSV, ready to open in a spreadsheet — also useful as evidence for an impact/grant report.') }}
        </p>
        <div class="flex flex-wrap gap-2">
          <button @click="downloadFile('/api/admin-ui/analytics/topics.csv', 'topic-stats.csv')"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            {{ tr('Topic solve rates') }}
          </button>
          <button @click="downloadFile('/api/admin-ui/analytics/users.csv', 'user-xp.csv')"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            {{ tr('User XP') }}
          </button>
          <button @click="downloadFile('/api/admin-ui/analytics/engagement.csv', 'weekly-engagement.csv')"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            {{ tr('Weekly engagement') }}
          </button>
        </div>
        <div class="mt-3 pt-3 border-t border-slate-100 dark:border-slate-800">
          <button @click="downloadFile('/api/admin-ui/analytics/report.xlsx', 'beecoding-report.xlsx')"
                  class="bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            <Icon name="chart" /> {{ tr('Export all as Excel (.xlsx)') }}
          </button>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-1">
            {{ tr('One workbook, three sheets — Topics, Users, Weekly engagement.') }}
          </p>
        </div>
      </div>

      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">{{ tr('System status') }}</h2>
          <button @click="loadSystemStatus" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>
        </div>
        <div v-if="systemStatus" class="grid sm:grid-cols-2 gap-x-6 gap-y-1.5 text-sm">
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Judge sandbox') }}</span>
            <span :class="systemStatus.bwrapUsable ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-700 dark:text-amber-400'">
              {{ systemStatus.sandboxMode }}
            </span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Sandbox required (fail closed)') }}</span>
            <span>{{ systemStatus.sandboxRequired ? tr('yes') : 'no' }}</span>
          </div>
          <div class="sm:col-span-2 flex justify-between gap-4 border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400 shrink-0">{{ tr('gcc (C)') }}</span>
            <span class="text-right font-mono text-xs">{{ systemStatus.gccVersion }}</span>
          </div>
          <div class="sm:col-span-2 flex justify-between gap-4 border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400 shrink-0">g++ (C++)</span>
            <span class="text-right font-mono text-xs">{{ systemStatus.gppVersion }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Judge queue backend') }}</span>
            <span>{{ systemStatus.judgeQueueBackend }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Pending judge jobs') }}</span>
            <span>{{ systemStatus.pendingJudgeJobs ?? 'n/a' }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Judge max concurrent') }}</span>
            <span>{{ systemStatus.judgeMaxConcurrent }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Judge compile timeout') }}</span>
            <span>{{ systemStatus.judgeCompileTimeoutMs }}ms</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Judge queue capacity') }}</span>
            <span>{{ systemStatus.judgeQueueCapacity }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Realtime backend') }}</span>
            <span>{{ systemStatus.realtimeBackend }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Redis') }}</span>
            <span v-if="!systemStatus.redisConfigured" class="text-slate-400">{{ tr('not used') }}</span>
            <span v-else :class="systemStatus.redisConnected ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'">
              {{ systemStatus.redisConnected ? tr('connected') : tr('disconnected') }}
            </span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Database') }}</span>
            <span :class="systemStatus.databaseOk ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'">
              {{ systemStatus.databaseOk ? tr('reachable') : tr('unreachable') }}
            </span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('LSP max concurrent sessions') }}</span>
            <span>{{ systemStatus.lspMaxConcurrent }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('LSP idle timeout') }}</span>
            <span>{{ systemStatus.lspIdleTimeoutSeconds }}s</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('LSP memory limit') }}</span>
            <span>{{ systemStatus.lspMemoryLimitMb > 0 ? systemStatus.lspMemoryLimitMb + 'MB' : tr('unset') }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Host (this instance)') }}</span>
            <span class="font-mono text-xs">{{ systemStatus.hostname }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('sysstat (sar)') }}</span>
            <span :class="systemStatus.sysstatInstalled ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">
              {{ systemStatus.sysstatInstalled ? tr('installed') : tr('not installed') }}
            </span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-slate-500 dark:text-slate-400">{{ tr('Checked') }}</span>
            <span class="text-[11px] text-slate-400">{{ when(systemStatus.checkedAt) }}</span>
          </div>
        </div>

        <div v-if="systemStatus?.sysstatInstalled" class="mt-4 pt-3 border-t border-slate-200 dark:border-slate-800">
          <div class="flex items-center gap-2 mb-2 flex-wrap">
            <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400">
              {{ tr('CPU / memory — {when}, host', { when: sysstatDaysAgo ? tr('{n} day(s) ago', { n: sysstatDaysAgo }) : tr('today') }) }} <span class="font-mono">{{ sysstatResult?.hostname }}</span> {{ tr('only') }}
            </h3>
            <select v-model.number="sysstatDaysAgo" @change="loadSysstat" class="ml-auto border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-xs">
              <option :value="0">{{ tr('Today') }}</option>
              <option v-for="d in 30" :key="d" :value="d">{{ tr('{n} day(s) ago', { n: d }) }}</option>
            </select>
            <span class="inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
              <button v-for="m in [['cpu', 'CPU'], ['mem', 'Memory']]" :key="m[0]"
                      @click="setSysstatMetric(m[0])" class="px-2.5 py-1"
                      :class="sysstatMetric === m[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
                {{ m[1] }}
              </button>
            </span>
          </div>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 mb-2">
            {{ tr('Host-local only — a load-balanced or split (web/judge) deployment has more than one machine, and this only ever shows whichever one answered this request. For a fleet-wide view, use a dedicated stack (e.g. Prometheus + node_exporter + Grafana).') }}
          </p>
          <p v-if="sysstatErr" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ sysstatErr }}</p>
          <MiniLineChart v-else-if="sysstatResult?.points?.length"
                         :title="sysstatMetric === 'cpu' ? tr('CPU used %') : tr('Memory used %')" :points="sysstatPoints()" />
          <p v-else class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No data for this day.') }}</p>
          <div class="flex items-center gap-2 mt-4 mb-2 flex-wrap">
            <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400">
              {{ sysstatMetric === 'cpu' ? tr('CPU used % — average vs peak') : tr('Memory used % — average vs peak') }}
              <span class="font-normal">{{ tr('(periods without saved data are skipped)') }}</span>
            </h3>
            <span class="ml-auto inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs">
              <button v-for="b in SYSSTAT_BUCKETS" :key="b[0]" @click="setSysstatBucket(b[0])" class="px-2.5 py-1"
                      :class="sysstatBucket === b[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
                {{ b[1] }}
              </button>
            </span>
          </div>
          <MiniLineChart v-if="sysstatDaily?.days?.length" :title="tr('Average vs peak %')" name="Avg" :points="dailyPoints('avg')"
                         :second="{ name: 'Peak', color: '#ef4444', points: dailyPoints('max') }" />
          <p v-else class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No saved data yet.') }}</p>
        </div>

        <div v-if="systemStatus" class="mt-4 pt-3 border-t border-slate-200 dark:border-slate-800">
          <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400 mb-2">
            {{ tr('Live settings') }} <span class="font-normal text-slate-500 dark:text-slate-400">{{ tr('— live, no restart needed') }}</span>
          </h3>
          <div class="grid gap-4 md:grid-cols-2">
            <section class="rounded-xl border border-slate-200 dark:border-slate-800 px-4 pt-3 pb-1">
              <h4 class="text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr('IntelliSense (clangd)') }}</h4>
              <div class="divide-y divide-slate-100 dark:divide-slate-800">
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('C/C++ IntelliSense enabled') }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('Autocomplete, hover and diagnostics in the editor. Off = word-based completion only.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="checkbox" v-model="runtimeConfigForm.lspEnabled" class="w-4 h-4" /></span>
              </label>
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('Share one clangd between students') }} <span v-if="systemStatus?.lspShared" class="text-xs text-slate-500 dark:text-slate-400">({{ systemStatus.lspSharedSessions }} / {{ systemStatus.lspSharedMax }} attached · {{ systemStatus.lspSharedProcesses }} / {{ systemStatus.lspSharedMaxProcesses }} process<template v-if="systemStatus.lspSharedMaxProcesses > 1">es</template>)</span></span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('One process serves the class instead of one per student: far less RAM, but a heavy request slows everyone. Open sessions keep their kind. The total and the process limit come from Lsp:SharedMaxTotalSessions / Lsp:SharedMaxProcesses.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="checkbox" v-model="runtimeConfigForm.lspShared" class="w-4 h-4" /></span>
              </label>
              </div>
            </section>
            <section class="rounded-xl border border-slate-200 dark:border-slate-800 px-4 pt-3 pb-1">
              <h4 class="text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr('Judge & problems') }}</h4>
              <div class="divide-y divide-slate-100 dark:divide-slate-800">
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('Submit / run cooldown') }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('Minimum gap between one user\'s runs or submissions.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="number" min="0" max="60000" step="100" v-model.number="runtimeConfigForm.judgeRateLimitMs"
                       class="w-24 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm text-right" /><span class="w-7 text-xs text-slate-500 dark:text-slate-400">ms</span></span>
              </label>
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('Max problems per student') }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('In a student\'s own bank. 0 stops students creating problems.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="number" min="0" max="1000" step="1" v-model.number="runtimeConfigForm.studentMaxProblems"
                       class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm text-right" /></span>
              </label>
              </div>
            </section>
            <section class="rounded-xl border border-slate-200 dark:border-slate-800 px-4 pt-3 pb-1">
              <h4 class="text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr('Statement images') }}</h4>
              <div class="divide-y divide-slate-100 dark:divide-slate-800">
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('Max image upload') }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('Larger files are refused; accepted ones are compressed (≤1200 px wide).') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="number" min="50" max="3000" step="50" v-model.number="runtimeConfigForm.imageMaxKb"
                       class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm text-right" /><span class="w-7 text-xs text-slate-500 dark:text-slate-400">KB</span></span>
              </label>
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('Max images per user') }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('0 turns picture uploads off.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="number" min="0" max="5000" step="10" v-model.number="runtimeConfigForm.imageMaxPerUser"
                       class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm text-right" /></span>
              </label>
              </div>
            </section>
            <section class="rounded-xl border border-slate-200 dark:border-slate-800 px-4 pt-3 pb-1">
              <h4 class="text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr('Notifications') }}</h4>
              <div class="divide-y divide-slate-100 dark:divide-slate-800">
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr('Delete read notifications after') }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('0 keeps them until the 30-day sweep. Unread ones: 30 days, newest 200 per person.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="number" min="0" max="365" step="1" v-model.number="runtimeConfigForm.notificationReadRetentionDays"
                       class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm text-right" /><span class="w-7 text-xs text-slate-500 dark:text-slate-400">{{ tr('days') }}</span></span>
              </label>
              </div>
            </section>
            <section class="rounded-xl border border-slate-200 dark:border-slate-800 px-4 pt-3 pb-1">
              <h4 class="text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr('Privacy') }}</h4>
              <div class="divide-y divide-slate-100 dark:divide-slate-800">
              <label v-for="f in [['boardCodeRetentionDays', 'Remove source code of board submissions after'], ['practiceCodeRetentionDays', 'Remove source code of practice submissions after']]" :key="f[0]" class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm text-slate-700 dark:text-slate-200">{{ tr(f[1]) }}</span>
                  <span class="block text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ tr('Only the code and compiler output are blanked, once, for good; verdicts, scores and XP stay. 0 keeps the code forever.') }}</span>
                </span>
                <span class="flex items-center gap-1.5 shrink-0"><input type="number" min="0" max="3650" step="1" v-model.number="runtimeConfigForm[f[0]]"
                       class="w-20 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded px-2 py-1 text-sm text-right" /><span class="w-7 text-xs text-slate-500 dark:text-slate-400">{{ tr('days') }}</span></span>
              </label>
              </div>
            </section>
          </div>
          <div class="mt-3 flex items-center gap-3">
            <button @click="saveRuntimeConfig" :disabled="runtimeConfigSaving"
                    class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
              {{ runtimeConfigSaving ? tr('Saving…') : tr('Save') }}
            </button>
            <span v-if="runtimeConfigMsg" class="text-xs text-emerald-600 dark:text-emerald-400">{{ runtimeConfigMsg }}</span>
          </div>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-2">
            {{ tr('Everything else above (sandbox mode, queue backend, gcc/g++) is fixed at deploy time.') }}
          </p>
        </div>

        <AdminNotifyKinds class="mt-4" />

        <ImageUsagePanel :usage="imageUsage" />

        <div v-if="latency" class="mt-4 pt-3 border-t border-slate-200 dark:border-slate-800">
          <div class="flex items-center gap-2 flex-wrap mb-2">
            <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400">
              {{ tr('Judging time') }} <span class="font-normal text-slate-500 dark:text-slate-400">{{ tr('— submit → verdict, queue wait included') }}</span>
            </h3>
            <div class="flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs ml-auto">
              <button v-for="h in [24, 48, 168]" :key="h" @click="setLatencyHours(h)" class="px-2.5 py-1"
                      :class="latencyHours === h ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400'">
                {{ h === 168 ? '7d' : h + 'h' }}
              </button>
            </div>
            <button @click="loadLatency" class="text-xs text-slate-500 dark:text-slate-400">↻</button>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 mb-2">
            {{ tr('{n} submissions · p95', { n: latency.total }) }} <span :class="latencyTone(latency.p95Seconds)">{{ latency.p95Seconds }}s</span>
            · {{ tr(latency.slots === 1 ? '{n} judge slot' : '{n} judge slots', { n: latency.slots }) }}
            <template v-if="latency.pendingNow != null"> · {{ tr('{n} waiting now', { n: latency.pendingNow }) }}</template>
          </p>
          <p v-if="latency.unfinished > 0" class="text-xs text-rose-600 dark:text-rose-400 mb-2">
            <Icon name="alert" /> {{ tr(latency.unfinished === 1 ? '{n} submission has been waiting for more than 2 minutes (oldest {min} min). Judging normally takes under a second, so these jobs were most likely lost when the app restarted; those students keep seeing “still checking” until they submit again.' : '{n} submissions have been waiting for more than 2 minutes (oldest {min} min). Judging normally takes under a second, so these jobs were most likely lost when the app restarted; those students keep seeing “still checking” until they submit again.', { n: latency.unfinished, min: Math.round(latency.oldestUnfinishedSeconds / 60) }) }}
          </p>
          <p v-if="!latency.buckets.length" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('No judged submissions in this window.') }}</p>
          <div v-else class="overflow-x-auto">
            <table class="w-full text-xs">
              <thead class="text-left text-slate-500 dark:text-slate-400">
                <tr><th class="py-1 pr-3 font-normal">{{ tr('Hour') }}</th><th class="pr-3 font-normal text-right">{{ tr('Submissions') }}</th>
                    <th class="pr-3 font-normal text-right">{{ tr('Avg') }}</th><th class="pr-3 font-normal text-right">p95</th><th class="font-normal text-right">{{ tr('Max') }}</th></tr>
              </thead>
              <tbody>
                <tr v-for="b in latency.buckets.slice().reverse()" :key="b.hourUtc" class="border-t border-slate-100 dark:border-slate-800 tabular-nums">
                  <td class="py-1 pr-3 whitespace-nowrap">{{ hourLabel(b.hourUtc) }}</td>
                  <td class="pr-3 text-right">{{ b.count }}</td>
                  <td class="pr-3 text-right" :class="latencyTone(b.avgSeconds)">{{ b.avgSeconds }}s</td>
                  <td class="pr-3 text-right" :class="latencyTone(b.p95Seconds)">{{ b.p95Seconds }}s</td>
                  <td class="text-right" :class="latencyTone(b.maxSeconds)">{{ b.maxSeconds }}s</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-2">
            {{ tr('A high p95 in a busy hour means submissions queued behind each other: raise') }} <code>Judge:MaxConcurrent</code> (default is half the CPUs) or add CPU.
            Amber ≥ 10s, red ≥ 30s.
          </p>
        </div>
      </div>
    </section>

    <!-- Trash: soft-deleted rows. Restore has no time limit here; Purge is permanent. -->
    <section v-show="tab === 'trash'" class="space-y-5">
      <button @click="loadTrash" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>

      <div v-for="[kind, label, labelFn] in TRASH_KINDS" :key="kind">
        <div class="flex items-center gap-3 mb-1.5 flex-wrap">
          <h2 class="font-semibold text-sm">{{ label }} · {{ trash[kind].total }}</h2>
          <div v-if="trash[kind].selected.size" class="flex items-center gap-2 text-xs">
            <span>{{ tr('{n} selected', { n: trash[kind].selected.size }) }}</span>
            <button @click="restoreSelectedTrash(kind)" class="row-action-btn row-action-btn--success">{{ tr('Restore selected') }}</button>
            <button @click="purgeSelectedTrash(kind)" class="row-action-btn row-action-btn--danger">{{ tr('Purge selected') }}</button>
          </div>
        </div>
        <p v-if="trash[kind].msg" class="text-[11px] text-emerald-600 dark:text-emerald-400 mb-1">{{ trash[kind].msg }}</p>

        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-1.5">
          <label v-if="trash[kind].rows?.length" class="flex items-center gap-2 text-[11px] text-slate-500 dark:text-slate-400 px-1">
            <input type="checkbox" :checked="trash[kind].selected.size === trash[kind].rows.length"
                   @change="selectAllTrash(kind, $event.target.checked)" /> {{ tr('Select all') }}
          </label>
          <div v-for="row in trash[kind].rows" :key="trashKey(kind, row)" class="border border-slate-200 dark:border-slate-800 rounded-lg p-2.5">
            <div class="flex items-start gap-2">
              <input type="checkbox" :checked="trash[kind].selected.has(trashKey(kind, row))" @change="toggleTrashSelect(kind, row)" class="mt-1 shrink-0" />
              <div class="min-w-0 flex-1">
                <div class="font-medium text-sm truncate">{{ labelFn(row) }}</div>
                <div class="text-[11px] text-slate-400">{{ row.email || row.ownerEmail || '' }} · {{ tr('deleted {when}', { when: when(row.deletedAt) }) }}</div>
              </div>
            </div>
            <div class="mt-1.5 flex gap-3">
              <button @click="restoreTrash(kind, row)" class="row-action-btn row-action-btn--success">{{ tr('Restore') }}</button>
              <button @click="purge(kind, row, labelFn(row))" class="row-action-btn row-action-btn--danger">{{ tr('Purge') }}</button>
            </div>
          </div>
          <p v-if="trash[kind].rows && !trash[kind].rows.length" class="text-slate-500 dark:text-slate-400 text-xs px-1">{{ tr('Empty.') }}</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1 pr-3 w-6">
                  <input type="checkbox" :checked="!!trash[kind].rows?.length && trash[kind].selected.size === trash[kind].rows.length"
                         @change="selectAllTrash(kind, $event.target.checked)" />
                </th>
                <th class="font-normal py-1 pr-3" colspan="4"></th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="row in trash[kind].rows" :key="trashKey(kind, row)" class="border-b border-slate-100 dark:border-slate-800/60">
                <td><input type="checkbox" :checked="trash[kind].selected.has(trashKey(kind, row))" @change="toggleTrashSelect(kind, row)" /></td>
                <td class="font-medium">{{ labelFn(row) }}</td>
                <td class="text-[11px] text-slate-400">{{ row.email || row.ownerEmail || '' }}</td>
                <td class="text-[11px] text-slate-400">{{ tr('deleted {when}', { when: when(row.deletedAt) }) }}</td>
                <td class="text-right">
                  <button @click="restoreTrash(kind, row)" class="row-action-btn row-action-btn--success mr-1">{{ tr('Restore') }}</button>
                  <button @click="purge(kind, row, labelFn(row))" class="row-action-btn row-action-btn--danger">{{ tr('Purge') }}</button>
                </td>
              </tr>
              <tr v-if="trash[kind].rows && !trash[kind].rows.length"><td colspan="5" class="text-slate-500 dark:text-slate-400 py-1.5 text-xs">{{ tr('Empty.') }}</td></tr>
            </tbody>
          </table>
        </div>
        <div v-if="trash[kind].total > trash[kind].pageSize" class="flex items-center gap-3 mt-1.5 text-xs">
          <span class="text-slate-500 dark:text-slate-400">
            {{ (trash[kind].page - 1) * trash[kind].pageSize + 1 }}–{{ Math.min(trash[kind].page * trash[kind].pageSize, trash[kind].total) }} of {{ trash[kind].total }}
          </span>
          <div class="ml-auto flex gap-2">
            <button @click="trashPrevPage(kind)" :disabled="trash[kind].page === 1"
                    class="px-2 py-0.5 rounded border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Prev') }}</button>
            <button @click="trashNextPage(kind)" :disabled="trash[kind].page * trash[kind].pageSize >= trash[kind].total"
                    class="px-2 py-0.5 rounded border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Next') }}</button>
          </div>
        </div>
      </div>
      <p v-if="TRASH_KINDS.every(([kind]) => trash[kind].rows === null)" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('Loading…') }}</p>
    </section>

    <!-- Audit log -->
    <section v-show="tab === 'audit'">
      <div class="flex flex-wrap gap-2 mb-3">
        <input v-model="auditQ" @keyup.enter="searchAudit" :placeholder="tr('Search actor, action, or target…')" :aria-label="tr('Search actor, action, or target…')"
               class="flex-1 min-w-0 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="searchAudit" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">{{ tr('Search') }}</button>
        <button @click="loadAudit" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('↻ refresh') }}</button>
      </div>

      <!-- mobile: cards -->
      <div v-if="tableView === 'card'" class="space-y-2">
        <div v-for="r in auditRows" :key="r.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-center justify-between gap-2 mb-1">
            <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span>
            <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</span>
          </div>
          <div class="text-xs">{{ r.actorEmail }}</div>
          <div class="text-[11px] text-slate-400 mt-0.5">{{ r.targetType }} · {{ r.targetLabel }}</div>
        </div>
        <p v-if="auditRows && !auditRows.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No activity yet.') }}</p>
      </div>

      <!-- desktop: table -->
      <div v-if="tableView === 'table'" class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">{{ tr('When') }}</th><th class="font-normal pr-3">{{ tr('Actor') }}</th>
              <th class="font-normal pr-3">{{ tr('Action') }}</th><th class="font-normal pr-3">{{ tr('Target') }}</th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="r in auditRows" :key="r.id" class="border-b border-slate-100 dark:border-slate-800/60">
              <td class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</td>
              <td class="text-[11px]">{{ r.actorEmail }}</td>
              <td>
                <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span>
              </td>
              <td class="text-[11px]">{{ r.targetType }} · {{ r.targetLabel }}</td>
            </tr>
            <tr v-if="auditRows && !auditRows.length"><td colspan="4" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No activity yet.') }}</td></tr>
          </tbody>
        </table>
      </div>
      <div v-if="auditTotal" class="flex items-center gap-3 mt-3 text-sm">
        <span class="text-slate-500 dark:text-slate-400">
          {{ (auditPage - 1) * auditPageSize + 1 }}–{{ Math.min(auditPage * auditPageSize, auditTotal) }} of {{ auditTotal }}
        </span>
        <div class="ml-auto flex gap-2">
          <button @click="auditPrevPage" :disabled="auditPage === 1"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Prev') }}</button>
          <button @click="auditNextPage" :disabled="auditPage * auditPageSize >= auditTotal"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">{{ tr('Next') }}</button>
        </div>
      </div>
    </section>

    <!-- Organizations: multi-tenant (separate universities/institutions) -->
    <section v-show="tab === 'organizations'" class="space-y-5">
      <p class="text-xs text-slate-500 dark:text-slate-400">
        {{ tr('Each organization gets its own boards, members, and AI quota — manage members/boards for one via') }} <RouterLink to="/org-admin" class="underline">{{ tr('Organization') }}</RouterLink>
        (this page only creates, renames and removes the organization itself).
      </p>
      <div class="flex flex-col sm:flex-row gap-2">
        <input v-model="orgForm.name" @keyup.enter="createOrganization" :placeholder="tr('Name (e.g. BINUS University)')" :aria-label="tr('Name')"
               class="flex-1 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <input v-model="orgForm.slug" @keyup.enter="createOrganization" :placeholder="tr('slug (e.g. binus)')" :aria-label="tr('slug')"
               class="sm:w-40 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="createOrganization" class="text-sm bg-amber-500 text-slate-900 rounded-lg px-4 py-2 sm:py-0 font-medium">{{ tr('Add') }}</button>
      </div>

      <!-- mobile: cards -->
      <div v-if="tableView === 'card'" class="space-y-2">
        <div v-for="o in organizations" :key="o.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <template v-if="orgEdit?.id === o.id">
            <div class="space-y-2">
              <input v-model="orgEdit.name" @keyup.enter="saveOrgEdit" @keyup.esc="orgEdit = null" :placeholder="tr('Name')" :aria-label="tr('Name')"
                     class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
              <input v-model="orgEdit.slug" @keyup.enter="saveOrgEdit" @keyup.esc="orgEdit = null" :placeholder="tr('slug')" :aria-label="tr('slug')"
                     class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
              <input v-model="orgEdit.emailDomains" @keyup.enter="saveOrgEdit" @keyup.esc="orgEdit = null" :placeholder="tr('auto-accept email domains, e.g. univ.ac.id, univ.edu')" :aria-label="tr('auto-accept email domains')"
                     class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
              <div class="flex gap-2">
                <button @click="saveOrgEdit" class="text-xs bg-amber-500 text-slate-900 rounded-lg px-3 py-1 font-medium">{{ tr('Save') }}</button>
                <button @click="orgEdit = null" class="row-action-btn">{{ tr('Cancel') }}</button>
              </div>
            </div>
          </template>
          <template v-else>
            <div class="flex items-start justify-between gap-2">
              <div class="font-medium text-sm">{{ o.name }}</div>
              <div class="flex gap-1 shrink-0">
                <button @click="startOrgEdit(o)" class="row-action-btn row-action-btn--accent">{{ tr('Edit') }}</button>
                <button @click="deleteOrganization(o)" class="row-action-btn row-action-btn--danger">{{ tr('Delete') }}</button>
              </div>
            </div>
            <div class="text-[11px] text-slate-400 mt-0.5">{{ o.slug }} · {{ tr('created {when}', { when: new Date(o.createdAt).toLocaleDateString() }) }}</div>
            <div class="text-[11px] text-slate-400">{{ o.emailDomains ? `auto-accepts ${o.emailDomains}` : tr('always invites') }}</div>
          </template>
        </div>
        <p v-if="organizations && !organizations.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No organizations yet.') }}</p>
      </div>

      <!-- desktop: table -->
      <div v-if="tableView === 'table'" class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">{{ tr('Name') }}</th><th class="font-normal pr-3">{{ tr('Slug') }}</th>
              <th class="font-normal pr-3">{{ tr('Auto-accept domains') }}</th><th class="font-normal pr-3">{{ tr('Created') }}</th><th class="font-normal pr-3"></th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="o in organizations" :key="o.id" class="border-b border-slate-100 dark:border-slate-800/60">
              <template v-if="orgEdit?.id === o.id">
                <td><input v-model="orgEdit.name" @keyup.enter="saveOrgEdit" @keyup.esc="orgEdit = null"
                           class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" /></td>
                <td><input v-model="orgEdit.slug" @keyup.enter="saveOrgEdit" @keyup.esc="orgEdit = null"
                           class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" /></td>
                <td><input v-model="orgEdit.emailDomains" @keyup.enter="saveOrgEdit" @keyup.esc="orgEdit = null" :placeholder="tr('univ.ac.id, univ.edu')" :aria-label="tr('univ.ac.id, univ.edu')"
                           class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm" /></td>
                <td></td>
                <td class="whitespace-nowrap">
                  <button @click="saveOrgEdit" class="text-xs bg-amber-500 text-slate-900 rounded-lg px-3 py-1 font-medium mr-1">{{ tr('Save') }}</button>
                  <button @click="orgEdit = null" class="row-action-btn">{{ tr('Cancel') }}</button>
                </td>
              </template>
              <template v-else>
                <td class="font-medium">{{ o.name }}</td>
                <td class="text-[11px] text-slate-400">{{ o.slug }}</td>
                <td class="text-[11px] text-slate-400">{{ o.emailDomains || '—' }}</td>
                <td class="text-[11px] text-slate-400">{{ new Date(o.createdAt).toLocaleDateString() }}</td>
                <td class="whitespace-nowrap">
                  <button @click="startOrgEdit(o)" class="row-action-btn row-action-btn--accent mr-1">{{ tr('Edit') }}</button>
                  <button @click="deleteOrganization(o)" class="row-action-btn row-action-btn--danger">{{ tr('Delete') }}</button>
                </td>
              </template>
            </tr>
            <tr v-if="organizations && !organizations.length"><td colspan="4" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No organizations yet.') }}</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- LTI 1.3: register LMS platforms as tool consumers -->
    <section v-show="tab === 'lti'" class="space-y-5">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">{{ tr('Tool configuration') }}</h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
          {{ tr('Give these to whoever registers BeeCoding as an external tool in the LMS. The LMS gives back an issuer, a client ID, and its own login/token/JWKS URLs — register the platform below with those.') }}
        </p>
        <div v-if="ltiToolConfig" class="space-y-1.5 text-xs">
          <div v-for="[label, value] in [
            ['OIDC login initiation URL', ltiToolConfig.loginInitiationUrl],
            ['Launch / redirect URL', ltiToolConfig.launchUrl],
            ['Public JWKS URL', ltiToolConfig.jwksUrl],
            ['Deep Linking URL', ltiToolConfig.deepLinkingUrl],
          ]" :key="label" class="flex items-center gap-2">
            <span class="w-40 shrink-0 text-slate-500 dark:text-slate-400">{{ label }}</span>
            <code class="flex-1 min-w-0 truncate bg-slate-100 dark:bg-slate-800 rounded px-2 py-1">{{ value }}</code>
            <button @click="copyLtiValue(value)" class="shrink-0 text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" :title="tr('Copy')">⧉</button>
          </div>
          <p v-if="ltiCopyMsg" class="text-emerald-600 dark:text-emerald-400">{{ ltiCopyMsg }}</p>
        </div>
      </div>

      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">{{ tr('Registered platforms') }}</h2>
          <button @click="startNewLtiPlatform" class="ml-auto text-xs bg-amber-500 text-slate-900 rounded-lg px-3 py-1 font-medium">{{ tr('+ Add platform') }}</button>
        </div>

        <div v-if="ltiEditing" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 mb-3 space-y-2 text-sm">
          <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400">{{ ltiEditing === 'new' ? tr('New platform') : tr('Edit platform') }}</h3>
          <input v-model="ltiForm.name" :placeholder="tr('Name (e.g. BINUS Moodle)')" :aria-label="tr('Name')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.issuer" :placeholder="tr('Issuer (iss)')" :aria-label="tr('Issuer (iss)')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.clientId" :placeholder="tr('Client ID')" :aria-label="tr('Client ID')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.deploymentIds" :placeholder="tr('Deployment ID(s), comma-separated')" :aria-label="tr('Deployment ID(s), comma-separated')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.authLoginUrl" :placeholder="tr('Auth login URL')" :aria-label="tr('Auth login URL')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.authTokenUrl" :placeholder="tr('Auth token URL')" :aria-label="tr('Auth token URL')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.jwksUrl" :placeholder="tr('JWKS (key set) URL')" :aria-label="tr('JWKS (key set) URL')" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <select v-model="ltiForm.organizationId" class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5">
            <option :value="null">{{ tr('No organization (unaffiliated)') }}</option>
            <option v-for="o in organizations" :key="o.id" :value="o.id">{{ o.name }}</option>
          </select>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 -mt-1">
            {{ tr('Launches through this platform auto-join the organization and auto-assign new boards to it.') }}
          </p>
          <label class="flex items-center gap-2 text-xs"><input type="checkbox" v-model="ltiForm.enabled" /> {{ tr('Enabled') }}</label>
          <div class="flex gap-2 pt-1">
            <button @click="saveLtiPlatform" class="bg-amber-500 text-slate-900 rounded-lg px-4 py-1.5 text-sm font-medium">{{ tr('Save') }}</button>
            <button @click="cancelLtiEdit" class="text-slate-500 dark:text-slate-400 px-3 text-sm">{{ tr('Cancel') }}</button>
          </div>
        </div>

        <!-- mobile: cards -->
        <div v-if="tableView === 'card'" class="space-y-2">
          <div v-for="p in ltiPlatforms" :key="p.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="flex items-start justify-between gap-2">
              <div class="font-medium text-sm">{{ p.name }}</div>
              <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap shrink-0"
                    :class="p.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300'">
                {{ p.enabled ? tr('enabled') : tr('disabled') }}
              </span>
            </div>
            <div class="text-[11px] text-slate-400 truncate mt-1">{{ p.issuer }}</div>
            <div class="text-[11px] text-slate-400 truncate">{{ p.clientId }}</div>
            <div class="text-[11px] text-slate-400 mt-0.5">{{ p.organizationName || tr('unaffiliated') }}</div>
            <div class="mt-2 flex gap-3">
              <button @click="startEditLtiPlatform(p)" class="row-action-btn row-action-btn--accent">{{ tr('Edit') }}</button>
              <button @click="deleteLtiPlatform(p)" class="row-action-btn row-action-btn--danger">{{ tr('Remove') }}</button>
            </div>
          </div>
          <p v-if="ltiPlatforms && !ltiPlatforms.length" class="text-slate-500 dark:text-slate-400 text-sm">{{ tr('No platforms registered yet.') }}</p>
        </div>

        <!-- desktop: table -->
        <div v-if="tableView === 'table'" class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">{{ tr('Name') }}</th><th class="font-normal pr-3">{{ tr('Issuer') }}</th>
                <th class="font-normal pr-3">{{ tr('Client ID') }}</th><th class="font-normal pr-3">{{ tr('Organization') }}</th>
                <th class="font-normal pr-3">{{ tr('Status') }}</th><th class="font-normal pr-3"></th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="p in ltiPlatforms" :key="p.id" class="border-b border-slate-100 dark:border-slate-800/60">
                <td class="font-medium">{{ p.name }}</td>
                <td class="text-[11px] text-slate-400 max-w-40 truncate">{{ p.issuer }}</td>
                <td class="text-[11px] text-slate-400 max-w-32 truncate">{{ p.clientId }}</td>
                <td class="text-[11px] text-slate-400">{{ p.organizationName || '—' }}</td>
                <td>
                  <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap"
                        :class="p.enabled ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300'">
                    {{ p.enabled ? tr('enabled') : tr('disabled') }}
                  </span>
                </td>
                <td class="whitespace-nowrap">
                  <button @click="startEditLtiPlatform(p)" class="row-action-btn row-action-btn--accent mr-1">{{ tr('Edit') }}</button>
                  <button @click="deleteLtiPlatform(p)" class="row-action-btn row-action-btn--danger">{{ tr('Remove') }}</button>
                </td>
              </tr>
              <tr v-if="ltiPlatforms && !ltiPlatforms.length"><td colspan="6" class="text-slate-500 dark:text-slate-400 py-3">{{ tr('No platforms registered yet.') }}</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>
      <!-- one-off password-reset link -->
    <div v-if="resetLink" role="dialog" aria-modal="true" :aria-label="tr('Password reset link')" class="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50" @click.self="resetLink = null">
      <div class="bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-lg p-5 shadow-lg space-y-3">
        <h2 class="font-semibold">{{ tr('Reset link for {name}', { name: resetLink.user.displayName }) }}</h2>
        <p class="text-sm text-slate-500 dark:text-slate-400">
          {{ tr('Send this to {email} yourself. It works once and expires {when}. Anyone with the link can set a new password — share it privately.', { email: resetLink.user.email, when: new Date(resetLink.expiresAt.endsWith('Z') ? resetLink.expiresAt : resetLink.expiresAt + 'Z').toLocaleString() }) }}
        </p>
        <input :value="resetLink.url" readonly @focus="$event.target.select()"
               class="w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-2 text-xs font-mono" />
        <div class="flex justify-end gap-2">
          <button @click="resetLink = null" class="px-3 py-1.5 rounded-lg text-sm text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">{{ tr('Close') }}</button>
          <button @click="copyResetLink" class="px-3 py-1.5 rounded-lg text-sm font-medium text-white bg-amber-500 hover:bg-amber-600">
            {{ resetLinkCopied ? tr('Copied ✓') : tr('Copy link') }}
          </button>
        </div>
      </div>
    </div>
</div>
</template>
