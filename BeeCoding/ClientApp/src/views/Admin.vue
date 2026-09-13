<script setup>
import { ref, reactive, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../lib/api';
import { withBase } from '../lib/base';
import { useAuth } from '../stores/auth';
import { useUndoToast } from '../stores/undoToast';
import { useConfirmDialog } from '../stores/confirmDialog';
import MiniLineChart from '../components/MiniLineChart.vue';
import TopicBarChart from '../components/TopicBarChart.vue';
import MarkdownBlock from '../components/MarkdownBlock.vue';

const auth = useAuth();
const route = useRoute();
const router = useRouter();
const confirmDialog = useConfirmDialog();
const undoToast = useUndoToast();
const tabDefs = [
  ['dashboard', 'Dashboard'], ['ai', 'AI'], ['users', 'Users'], ['boards', 'Boards'], ['problems', 'Problems'],
  ['review', 'AI review'], ['reports', 'Reports'], ['trash', 'Trash'], ['audit', 'Audit log'],
  ['organizations', 'Organizations'], ['lti', 'LTI'],
];
// Deep-linkable: /admin/users etc. — reload/share/bookmark lands on the same tab.
const tab = ref(tabDefs.some((t) => t[0] === route.params.tab) ? route.params.tab : 'dashboard');
const err = ref('');

const aiRows = ref(null);
const users = ref(null);
const userQ = ref('');
const usersPage = ref(1);
const usersPageSize = ref(25);
const usersTotal = ref(0);

const fmt = (n) => (n ?? 0).toLocaleString();
const when = (d) => new Date(d.endsWith('Z') ? d : d + 'Z').toLocaleString();
const actionBadgeClass = (action) => ({
  'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300': ['delete', 'admin-revoke', 'ai-ban', 'ai-pause', 'ai-review-reject'].includes(action),
  'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300': ['restore', 'create', 'ai-resume', 'ai-review-approve'].includes(action),
  'bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300': ['purge', 'ai-override-clear'].includes(action),
  'bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300': ['admin-grant', 'role-change', 'ai-quota-set'].includes(action),
});

function loadTabData(id) {
  if (id === 'dashboard') { if (!dashboard.value) loadDashboard(); }
  else if (id === 'ai') {
    if (!aiRows.value) loadAi();
    if (!aiSettings.value) loadAiSettings();
    if (!aiProvider.value) loadAiProvider();
    if (!aiOverrides.value) loadAiOverrides();
  } else if (id === 'users') {
    if (!users.value) loadUsers();
    if (!boards.value) loadBoards();   // populates the CSV-import board picker too
  } else if (id === 'boards' && !boards.value) loadBoards();
  else if (id === 'review') loadAiReview();   // queue changes often — always refresh
  else if (id === 'reports' && !systemStatus.value) loadSystemStatus();
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
  router.replace(`/admin/${id}`);
  loadTabData(id);
}
// Browser back/forward (or a direct link to /admin/<tab>) changes route.params.tab
// without going through switchTab — keep the active tab (and its data) in sync.
watch(() => route.params.tab, (t) => {
  const id = tabDefs.some(([k]) => k === t) ? t : 'dashboard';
  if (id !== tab.value) { tab.value = id; loadTabData(id); }
});

// ---- dashboard: at-a-glance overview, the default landing tab ----
const dashboard = ref(null);
const weeklyStats = ref(null);
const topicStats = ref(null);
async function loadDashboard() {
  err.value = '';
  try {
    const [d, weekly, topics] = await Promise.all([
      api.get('/api/admin-ui/dashboard'),
      api.get('/api/admin-ui/dashboard/weekly?weeks=12'),
      api.get('/api/admin-ui/dashboard/topics?take=8'),
    ]);
    dashboard.value = d;
    weeklyStats.value = weekly;
    topicStats.value = topics;
  } catch (e) { err.value = e.message; }
}
const shortDate = (s) => new Date(`${s}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const activeUserPoints = () => (weeklyStats.value || []).map((w) => ({ label: shortDate(w.weekStart), value: w.activeUsers }));
const submissionPoints = () => (weeklyStats.value || []).map((w) => ({ label: shortDate(w.weekStart), value: w.submissions }));
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
async function clearAiProviderKey() {
  if (!(await confirmDialog.ask('Clear the saved API key? AI will fall back to appsettings.json\'s key (if any).', { confirmLabel: 'Clear' }))) return;
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
  try { aiRows.value = await api.get('/api/admin-ui/ai-usage'); }
  catch (e) { err.value = e.message; }
}
async function loadUsers() {
  err.value = '';
  selectedUsers.value = new Set();
  try {
    const p = new URLSearchParams({ page: String(usersPage.value), pageSize: String(usersPageSize.value) });
    if (userQ.value.trim()) p.set('q', userQ.value.trim());
    const result = await api.get(`/api/admin-ui/users?${p}`);
    users.value = result.rows;
    usersTotal.value = result.total;
  } catch (e) { err.value = e.message; }
}
function searchUsers() { usersPage.value = 1; loadUsers(); }
function usersPrevPage() { if (usersPage.value > 1) { usersPage.value--; loadUsers(); } }
function usersNextPage() { if (usersPage.value * usersPageSize.value < usersTotal.value) { usersPage.value++; loadUsers(); } }

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
  if (!(await confirmDialog.ask(`Delete ${ids.length} user(s)? They'll move to Trash and can be restored there.`, { confirmLabel: 'Delete' }))) return;
  err.value = ''; userDeleteResultMsg.value = '';
  try {
    const result = await api.post('/api/admin-ui/users/bulk-delete', { ids });
    selectedUsers.value = new Set();
    await loadUsers();
    userDeleteResultMsg.value = `Deleted ${result.deleted} user(s).` + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
  } catch (e) { err.value = e.message; }
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
async function archiveSelectedBoards() {
  const slugs = [...selectedBoards.value];
  if (!slugs.length) return;
  if (!(await confirmDialog.ask(`Archive ${slugs.length} board(s)? They'll move to Trash and can be restored there (no 30s limit for admins).`, { confirmLabel: 'Archive' }))) return;
  err.value = ''; archiveResultMsg.value = '';
  try {
    const result = await api.post('/api/admin-ui/boards/archive', { slugs });
    selectedBoards.value = new Set();
    await loadBoards();
    archiveResultMsg.value = `Archived ${result.archived} board(s).` + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
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
  try {
    const p = new URLSearchParams({ page: String(st.page), pageSize: String(st.pageSize) });
    const result = await api.get(`/api/admin-ui/trash/${kind}?${p}`);
    st.rows = result.rows;
    st.total = result.total;
    st.selected = new Set();
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
  if (!(await confirmDialog.ask(`Permanently delete "${label}"? This cannot be undone.`, { confirmLabel: 'Delete forever' }))) return;
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
  if (!(await confirmDialog.ask(`Permanently delete ${ids.length} item(s)? This cannot be undone.`, { confirmLabel: 'Delete forever' }))) return;
  err.value = ''; trash[kind].msg = '';
  try {
    const result = await api.post(`/api/admin-ui/trash/${kind}/purge`, { ids });
    await loadTrashKind(kind);
    trash[kind].msg = `Purged ${result.count}.` + (result.errors.length ? ` Errors: ${result.errors.join(', ')}` : '');
  } catch (e) { err.value = e.message; }
}

// ---- audit log ----
const auditRows = ref(null);
const auditQ = ref('');
const auditPage = ref(1);
const auditPageSize = ref(50);
const auditTotal = ref(0);

async function loadAudit() {
  err.value = '';
  try {
    const p = new URLSearchParams({ page: String(auditPage.value), pageSize: String(auditPageSize.value) });
    if (auditQ.value.trim()) p.set('q', auditQ.value.trim());
    const result = await api.get(`/api/admin-ui/audit-log?${p}`);
    auditRows.value = result.rows;
    auditTotal.value = result.total;
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
async function deleteOrganization(o) {
  if (!(await confirmDialog.ask(`Delete organization "${o.name}"? Its boards/LTI platforms stay but become unaffiliated; members lose Org Admin access to it.`, { confirmLabel: 'Delete' }))) return;
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
  if (!(await confirmDialog.ask(`Remove platform "${p.name}"? Existing linked users/boards stay, but it can no longer launch.`, { confirmLabel: 'Remove' }))) return;
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
const exportProblems = () => downloadFile('/api/admin-ui/problems/export', 'beecoding-problems.json');

// ---- reports: analytics export + system status ----
const systemStatus = ref(null);
async function loadSystemStatus() {
  err.value = '';
  try { systemStatus.value = await api.get('/api/admin-ui/system-status'); }
  catch (e) { err.value = e.message; }
}

async function importProblems(ev) {
  const file = ev.target.files?.[0];
  ev.target.value = '';
  if (!file) return;
  err.value = ''; importResult.value = null; importing.value = true;
  try {
    const bundle = JSON.parse(await file.text());
    importResult.value = await api.post(
      `/api/admin-ui/problems/import?replaceExisting=${replaceExisting.value}`, bundle);
  } catch (e) { err.value = e.message; }
  finally { importing.value = false; }
}

onMounted(async () => {
  if (!auth.user?.isAdmin) { router.replace('/boards'); return; }
  if (route.params.tab !== tab.value) router.replace(`/admin/${tab.value}`);
  loadTabData(tab.value);
});
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8">
    <h1 class="text-xl font-bold mb-4">Admin</h1>

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
        <button v-for="t in tabDefs" :key="t[0]" @click="switchTab(t[0])"
                class="whitespace-nowrap text-left md:text-center rounded-lg px-3 py-1.5"
                :class="tab === t[0] ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
          {{ t[1] }}
        </button>
      </div>
    </div>

    <p v-if="err" class="text-sm text-red-600 dark:text-red-400 mb-3">{{ err }}</p>

    <!-- Dashboard: at-a-glance overview -->
    <section v-show="tab === 'dashboard'" class="space-y-5">
      <button @click="loadDashboard" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>

      <div v-if="dashboard" class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
        <button type="button" @click="switchTab('users')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-400 dark:text-slate-500">Users</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalUsers) }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
            {{ dashboard.teacherCount }} teacher · {{ dashboard.studentCount }} student · {{ dashboard.adminCount }} admin
          </div>
        </button>

        <button type="button" @click="switchTab('boards')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-400 dark:text-slate-500">Boards</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalBoards) }}</div>
        </button>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-400 dark:text-slate-500">Problems</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalProblems + dashboard.totalBankProblems) }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
            {{ fmt(dashboard.totalProblems) }} on boards · {{ fmt(dashboard.totalBankProblems) }} in bank
          </div>
        </div>

        <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
          <div class="text-xs text-slate-400 dark:text-slate-500">Submissions</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.totalSubmissions) }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
            {{ dashboard.totalSubmissions ? Math.round(100 * dashboard.acceptedSubmissions / dashboard.totalSubmissions) : 0 }}% accepted
          </div>
        </div>

        <button type="button" @click="switchTab('review')" class="text-left border rounded-xl p-4"
                :class="dashboard.pendingAiReview
                  ? 'border-amber-300 dark:border-amber-500/40 bg-amber-50/50 dark:bg-amber-500/5 hover:border-amber-400'
                  : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'">
          <div class="text-xs text-slate-400 dark:text-slate-500">Pending AI review</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.pendingAiReview) }}</div>
        </button>

        <button type="button" @click="switchTab('trash')" class="text-left border rounded-xl p-4"
                :class="dashboard.trashCount
                  ? 'border-rose-300 dark:border-rose-500/40 bg-rose-50/50 dark:bg-rose-500/5 hover:border-rose-400'
                  : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'">
          <div class="text-xs text-slate-400 dark:text-slate-500">In trash</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.trashCount) }}</div>
        </button>

        <button type="button" @click="switchTab('ai')" class="text-left border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700">
          <div class="text-xs text-slate-400 dark:text-slate-500">AI calls today / this month</div>
          <div class="text-2xl font-bold">{{ fmt(dashboard.aiToday.calls) }} / {{ fmt(dashboard.aiMonth.calls) }}</div>
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

      <div v-if="dashboard">
        <div class="flex items-center gap-2 mb-1.5">
          <h2 class="font-semibold text-sm">Recent activity</h2>
          <button @click="switchTab('audit')" class="text-xs text-slate-400 dark:text-slate-500 hover:underline">view all</button>
        </div>
        <!-- mobile: cards -->
        <div class="sm:hidden space-y-2">
          <div v-for="r in dashboard.recentActivity" :key="r.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="flex items-center justify-between gap-2 mb-1">
              <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span>
              <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</span>
            </div>
            <div class="text-xs">{{ r.actorEmail }}</div>
            <div class="text-[11px] text-slate-400 mt-0.5">{{ r.targetType }} · {{ r.targetLabel }}</div>
          </div>
          <p v-if="!dashboard.recentActivity.length" class="text-slate-400 dark:text-slate-500 text-sm">No activity yet.</p>
        </div>

        <!-- desktop: table -->
        <div class="hidden sm:block overflow-x-auto">
          <table class="w-full text-sm">
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="r in dashboard.recentActivity" :key="r.id" class="border-b border-slate-100 dark:border-slate-800/60">
                <td class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</td>
                <td class="text-[11px]">{{ r.actorEmail }}</td>
                <td><span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span></td>
                <td class="text-[11px]">{{ r.targetType }} · {{ r.targetLabel }}</td>
              </tr>
              <tr v-if="!dashboard.recentActivity.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No activity yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <!-- AI usage -->
    <section v-show="tab === 'ai'" class="space-y-5">
      <!-- Kill-switch + default daily quotas -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4" v-if="aiSettings">
        <h2 class="font-semibold text-sm mb-2">Global AI settings</h2>
        <label class="flex items-center gap-2 text-sm mb-2">
          <input type="checkbox" v-model="aiSettings.paused" />
          <span :class="aiSettings.paused ? 'text-rose-600 dark:text-rose-400 font-medium' : ''">
            Pause AI platform-wide {{ aiSettings.paused ? '(paused — no AI feature works right now)' : '' }}
          </span>
        </label>
        <input v-model="aiSettings.pausedReason" placeholder="Reason (shown in the audit log only)"
               class="w-full mb-3 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
        <div class="flex flex-wrap gap-4 mb-3 text-sm">
          <label class="flex items-center gap-2">Daily quota · students
            <input v-model.number="aiSettings.dailyQuotaStudent" type="number" min="0"
                   class="w-20 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
          <label class="flex items-center gap-2">Daily quota · teachers
            <input v-model.number="aiSettings.dailyQuotaTeacher" type="number" min="0"
                   class="w-20 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1" />
          </label>
        </div>
        <p class="text-[11px] text-slate-400 dark:text-slate-500 mb-3">
          Requests per user per calendar day (UTC), across hints + problem generation combined.
          Takes effect on the next AI request platform-wide — no restart needed.
        </p>
        <button @click="saveAiSettings" :disabled="aiSettingsSaving"
                class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ aiSettingsSaving ? 'Saving…' : 'Save' }}
        </button>
      </div>

      <!-- Which AI provider/credential is billed -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4" v-if="aiProvider">
        <h2 class="font-semibold text-sm mb-1">AI provider (platform default)</h2>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-3">
          Overrides appsettings.json's <code>Ai:*</code> at runtime. An organization can set
          its own key/model in <RouterLink to="/org-admin" class="underline">Organization</RouterLink>
          → AI settings, which takes priority over this for that organization only.
        </p>
        <div class="space-y-2 text-sm max-w-md">
          <label class="flex flex-col gap-1">
            <span class="text-xs text-slate-400 dark:text-slate-500">
              API key {{ aiProvider.hasApiKey ? `(saved: ${aiProvider.apiKeyPreview})` : '(none saved — using appsettings.json, if any)' }}
            </span>
            <div class="flex gap-2">
              <input v-model="aiProviderForm.apiKey" type="password" placeholder="Leave blank to keep the saved key"
                     class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
              <button v-if="aiProvider.hasApiKey" @click="clearAiProviderKey" class="text-xs text-rose-600 dark:text-rose-400 hover:underline shrink-0">Clear</button>
            </div>
          </label>
          <input v-model="aiProviderForm.baseUrl" placeholder="Base URL (blank = appsettings.json default)"
                 class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="aiProviderForm.model" placeholder="Model (blank = appsettings.json default)"
                 class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="aiProviderForm.generateModel" placeholder="Generate-problem model (blank = same as above)"
                 class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
        </div>
        <p v-if="aiProviderMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mt-2">{{ aiProviderMsg }}</p>
        <button @click="saveAiProvider" :disabled="aiProviderSaving"
                class="mt-3 bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
          {{ aiProviderSaving ? 'Saving…' : 'Save' }}
        </button>
      </div>

      <!-- Per-user overrides / bans -->
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-2">Per-user overrides</h2>
        <!-- mobile: cards -->
        <div class="sm:hidden space-y-2 mb-3">
          <div v-for="row in aiOverrides" :key="row.userId" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="font-medium text-sm">{{ row.displayName }}</div>
            <div class="text-[11px] text-slate-400 mb-2">{{ row.email }}</div>
            <div class="flex items-center gap-3 flex-wrap text-xs">
              <label class="flex items-center gap-1.5">Quota
                <input v-model.number="row.dailyQuotaOverride" type="number" min="0" placeholder="role default"
                       class="w-24 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 text-xs" />
              </label>
              <label class="flex items-center gap-1.5"><input type="checkbox" v-model="row.banned" /> Banned</label>
            </div>
            <div class="mt-2 flex gap-3">
              <button @click="saveOverride(row)" class="text-[11px] text-emerald-600 dark:text-emerald-400 hover:underline">Save</button>
              <button @click="clearOverride(row)" class="text-[11px] text-slate-500 dark:text-slate-400 hover:underline">Clear</button>
            </div>
          </div>
          <p v-if="aiOverrides && !aiOverrides.length" class="text-slate-400 dark:text-slate-500 text-sm">No overrides set.</p>
        </div>

        <!-- desktop: table -->
        <div class="hidden sm:block overflow-x-auto mb-3">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">User</th><th class="font-normal pr-3">Daily quota</th>
                <th class="font-normal pr-3">Banned</th><th class="font-normal pr-3"></th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
              <tr v-for="row in aiOverrides" :key="row.userId" class="border-b border-slate-100 dark:border-slate-800/60">
                <td><div class="font-medium">{{ row.displayName }}</div><div class="text-[11px] text-slate-400">{{ row.email }}</div></td>
                <td>
                  <input v-model.number="row.dailyQuotaOverride" type="number" min="0" placeholder="role default"
                         class="w-24 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 text-xs" />
                </td>
                <td><input type="checkbox" v-model="row.banned" /></td>
                <td class="whitespace-nowrap">
                  <button @click="saveOverride(row)" class="text-[11px] text-emerald-600 dark:text-emerald-400 hover:underline mr-3">Save</button>
                  <button @click="clearOverride(row)" class="text-[11px] text-slate-500 dark:text-slate-400 hover:underline">Clear</button>
                </td>
              </tr>
              <tr v-if="aiOverrides && !aiOverrides.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No overrides set.</td></tr>
            </tbody>
          </table>
        </div>
        <div class="flex flex-wrap items-end gap-2 text-sm">
          <label class="flex flex-col text-xs text-slate-400 dark:text-slate-500">User ID
            <input v-model.number="newOverride.userId" type="number" placeholder="see Users tab"
                   class="w-28 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 text-sm" />
          </label>
          <label class="flex flex-col text-xs text-slate-400 dark:text-slate-500">Daily quota
            <input v-model.number="newOverride.quota" type="number" min="0" placeholder="role default"
                   class="w-28 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 text-sm" />
          </label>
          <label class="flex items-center gap-1.5"><input type="checkbox" v-model="newOverride.banned" /> Banned</label>
          <button @click="addOverride" :disabled="!newOverride.userId"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">
            Add / update
          </button>
        </div>
      </div>

      <!-- Usage -->
      <div>
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">Usage</h2>
          <button @click="loadAi" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>
        </div>
        <!-- mobile: cards -->
        <div class="sm:hidden space-y-2">
          <div v-for="r in aiRows" :key="r.userId" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
            <div class="font-medium text-sm">{{ r.displayName }}</div>
            <div class="text-[11px] text-slate-400 mb-1.5">{{ r.email }}</div>
            <div class="text-xs grid grid-cols-3 gap-2 tabular-nums">
              <div><div class="text-[10px] text-slate-400">Today</div>{{ fmt(r.today.calls) }} / {{ fmt(r.today.totalTokens) }}</div>
              <div><div class="text-[10px] text-slate-400">Month</div>{{ fmt(r.month.calls) }} / {{ fmt(r.month.totalTokens) }}</div>
              <div><div class="text-[10px] text-slate-400">All time</div><span class="font-medium">{{ fmt(r.allTime.calls) }} / {{ fmt(r.allTime.totalTokens) }}</span></div>
            </div>
          </div>
          <p v-if="aiRows && !aiRows.length" class="text-slate-400 dark:text-slate-500 text-sm">No AI usage yet.</p>
        </div>

        <!-- desktop: table -->
        <div class="hidden sm:block overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">User</th>
                <th class="font-normal px-2">Today (calls / tokens)</th>
                <th class="font-normal px-2">This month</th>
                <th class="font-normal px-2">All time</th>
              </tr>
            </thead>
            <tbody class="[&_td]:py-1.5 [&_td]:pr-3 [&_td]:align-top">
              <tr v-for="r in aiRows" :key="r.userId" class="border-b border-slate-100 dark:border-slate-800/60">
                <td><div class="font-medium">{{ r.displayName }}</div><div class="text-[11px] text-slate-400">{{ r.email }}</div></td>
                <td class="tabular-nums">{{ fmt(r.today.calls) }} / {{ fmt(r.today.totalTokens) }}</td>
                <td class="tabular-nums">{{ fmt(r.month.calls) }} / {{ fmt(r.month.totalTokens) }}</td>
                <td class="tabular-nums font-medium">{{ fmt(r.allTime.calls) }} / {{ fmt(r.allTime.totalTokens) }}</td>
              </tr>
              <tr v-if="aiRows && !aiRows.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No AI usage yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <!-- Users -->
    <section v-show="tab === 'users'">
      <div class="flex gap-2 mb-3">
        <input v-model="userQ" @keyup.enter="searchUsers" placeholder="Search name or email…"
               class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="searchUsers" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">Search</button>
      </div>
      <div v-if="selectedUsers.size" class="flex items-center gap-2 mb-2 text-sm">
        <span>{{ selectedUsers.size }} selected</span>
        <button @click="deleteSelectedUsers" class="bg-rose-600 hover:bg-rose-700 text-white rounded-lg px-3 py-1 text-xs font-medium">
          Delete selected
        </button>
      </div>
      <p v-if="userDeleteResultMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mb-2">{{ userDeleteResultMsg }}</p>

      <!-- mobile: cards -->
      <div class="sm:hidden space-y-2">
        <label v-if="users?.length" class="flex items-center gap-2 text-xs text-slate-400 dark:text-slate-500 px-1">
          <input type="checkbox" :checked="selectedUsers.size === users.filter((u) => u.id !== auth.user?.id).length"
                 @change="selectAllUsers($event.target.checked)" /> Select all
        </label>
        <div v-for="u in users" :key="u.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-start gap-2">
            <input v-if="u.id !== auth.user?.id" type="checkbox" :checked="selectedUsers.has(u.id)" @change="toggleUserSelect(u.id)" class="mt-1 shrink-0" />
            <div class="min-w-0 flex-1">
              <div class="font-medium text-sm truncate">{{ u.displayName }} <span class="text-slate-400 font-normal">#{{ u.id }}</span></div>
              <div class="text-[11px] text-slate-400 truncate">{{ u.email }}</div>
            </div>
          </div>
          <div class="flex items-center gap-2 flex-wrap mt-2">
            <select v-if="u.id !== auth.user?.id" :value="u.role" @change="changeRole(u, $event.target.value)"
                    class="text-[11px] border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-1 py-0.5">
              <option value="Student">Student</option>
              <option value="Teacher">Teacher</option>
            </select>
            <span v-else class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300">{{ u.role }}</span>
            <span v-if="u.isAdmin" class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300">admin</span>
            <span class="text-[11px] text-slate-400 tabular-nums">{{ fmt(u.xp) }} XP · {{ u.ownedBoards }} boards · {{ u.submissions }} subs</span>
          </div>
          <div class="text-[11px] text-slate-400 mt-1">Joined {{ new Date(u.createdAt).toLocaleDateString() }}</div>
          <div v-if="u.id !== auth.user?.id" class="mt-2 flex gap-3">
            <button v-if="!u.isAdmin" @click="grantAdmin(u)" class="text-[11px] text-violet-600 dark:text-violet-400 hover:underline">Make admin</button>
            <button v-else @click="revokeAdmin(u)" class="text-[11px] text-slate-500 dark:text-slate-400 hover:underline">Revoke admin</button>
            <button @click="deleteUser(u)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Delete</button>
          </div>
        </div>
        <p v-if="users && !users.length" class="text-slate-400 dark:text-slate-500 text-sm">No users.</p>
      </div>

      <!-- desktop: table -->
      <div class="hidden sm:block overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">
                <input type="checkbox" :checked="!!users?.length && selectedUsers.size === users.filter((u) => u.id !== auth.user?.id).length"
                       @change="selectAllUsers($event.target.checked)" />
              </th>
              <th class="font-normal py-1.5 pr-3">#</th><th class="font-normal pr-3">Name / email</th>
              <th class="font-normal pr-3">Role</th><th class="font-normal pr-3">XP</th>
              <th class="font-normal pr-3">Boards</th><th class="font-normal pr-3">Subs</th>
              <th class="font-normal pr-3">Joined</th><th class="font-normal pr-3"></th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="u in users" :key="u.id" class="border-b border-slate-100 dark:border-slate-800/60">
              <td><input v-if="u.id !== auth.user?.id" type="checkbox" :checked="selectedUsers.has(u.id)" @change="toggleUserSelect(u.id)" /></td>
              <td class="text-slate-400">{{ u.id }}</td>
              <td><div class="font-medium">{{ u.displayName }}</div><div class="text-[11px] text-slate-400">{{ u.email }}</div></td>
              <td>
                <select v-if="u.id !== auth.user?.id" :value="u.role" @change="changeRole(u, $event.target.value)"
                        class="text-[11px] border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-1 py-0.5">
                  <option value="Student">Student</option>
                  <option value="Teacher">Teacher</option>
                </select>
                <span v-else class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300">{{ u.role }}</span>
                <span v-if="u.isAdmin" class="ml-1 text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300">admin</span>
              </td>
              <td class="tabular-nums">{{ fmt(u.xp) }}</td>
              <td class="tabular-nums">{{ u.ownedBoards }}</td>
              <td class="tabular-nums">{{ u.submissions }}</td>
              <td class="text-[11px] text-slate-400">{{ new Date(u.createdAt).toLocaleDateString() }}</td>
              <td class="whitespace-nowrap">
                <template v-if="u.id !== auth.user?.id">
                  <button v-if="!u.isAdmin" @click="grantAdmin(u)"
                          class="text-[11px] text-violet-600 dark:text-violet-400 hover:underline mr-3">Make admin</button>
                  <button v-else @click="revokeAdmin(u)"
                          class="text-[11px] text-slate-500 dark:text-slate-400 hover:underline mr-3">Revoke admin</button>
                  <button @click="deleteUser(u)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Delete</button>
                </template>
              </td>
            </tr>
            <tr v-if="users && !users.length"><td colspan="9" class="text-slate-400 dark:text-slate-500 py-3">No users.</td></tr>
          </tbody>
        </table>
      </div>
      <div v-if="usersTotal" class="flex items-center gap-3 mt-3 text-sm">
        <span class="text-slate-400 dark:text-slate-500">
          {{ (usersPage - 1) * usersPageSize + 1 }}–{{ Math.min(usersPage * usersPageSize, usersTotal) }} of {{ usersTotal }}
        </span>
        <div class="ml-auto flex gap-2">
          <button @click="usersPrevPage" :disabled="usersPage === 1"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
          <button @click="usersNextPage" :disabled="usersPage * usersPageSize >= usersTotal"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
        </div>
      </div>

      <div class="mt-5 border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">Bulk import (CSV)</h2>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">
          Header row required — at least <code>email</code> and <code>name</code> (or
          <code>displayname</code>); optional <code>role</code> and <code>password</code>
          columns. An email that already exists is left alone (just optionally added to the
          board below). A missing password is auto-generated and shown once — copy it down.
        </p>
        <div class="flex flex-wrap gap-2 mb-2 text-sm">
          <select v-model="importDefaultRole" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm">
            <option value="Student">Default role: Student</option>
            <option value="Teacher">Default role: Teacher</option>
          </select>
          <select v-model="importBoardSlug" class="flex-1 min-w-48 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-1 text-sm">
            <option value="">Don't assign to a board</option>
            <option v-for="b in boards" :key="b.slug" :value="b.slug">Add to: {{ b.title }}</option>
          </select>
        </div>
        <input type="file" accept="text/csv,.csv" @change="importUsersCsv" :disabled="importingUsers" class="text-sm" />
        <p v-if="importingUsers" class="text-xs text-slate-400 mt-2">Importing…</p>
        <div v-if="userImportResult" class="mt-3 text-sm">
          <p class="text-emerald-600 dark:text-emerald-400">
            Created {{ userImportResult.created }} · Already existed {{ userImportResult.existing }} · Errors {{ userImportResult.errors }}
          </p>
          <div class="overflow-x-auto mt-2">
            <table class="w-full text-xs">
              <thead>
                <tr class="text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                  <th class="font-normal py-1 pr-3">Email</th><th class="font-normal pr-3">Name</th>
                  <th class="font-normal pr-3">Password</th><th class="font-normal pr-3">Note</th>
                </tr>
              </thead>
              <tbody class="[&_td]:py-1 [&_td]:pr-3">
                <tr v-for="(row, i) in userImportResult.rows" :key="i" class="border-b border-slate-100 dark:border-slate-800/60">
                  <td>{{ row.email }}</td>
                  <td>{{ row.displayName }}</td>
                  <td class="font-mono">{{ row.generatedPassword || '—' }}</td>
                  <td :class="row.error ? 'text-red-600 dark:text-red-400' : 'text-slate-400'">
                    {{ row.error || (row.addedToBoard ? 'added to board' : (row.created ? 'created' : 'already existed')) }}
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
        <input v-model="boardQ" @keyup.enter="loadBoards" placeholder="Search title or slug…"
               class="flex-1 min-w-40 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="loadBoards" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-2">Search</button>
        <span class="text-slate-300 dark:text-slate-700">|</span>
        <label class="text-xs text-slate-500 dark:text-slate-400 flex items-center gap-1">Select boards created before
          <input type="date" v-model="archiveBeforeDate" class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded px-2 py-1 text-sm" />
        </label>
        <button @click="selectBoardsBeforeDate" class="text-xs text-slate-500 dark:text-slate-400 hover:underline">select</button>
      </div>
      <div v-if="selectedBoards.size" class="flex items-center gap-2 mb-2 text-sm">
        <span>{{ selectedBoards.size }} selected</span>
        <button @click="archiveSelectedBoards" class="bg-rose-600 hover:bg-rose-700 text-white rounded-lg px-3 py-1 text-xs font-medium">
          🗄️ Archive selected
        </button>
      </div>
      <p v-if="archiveResultMsg" class="text-xs text-emerald-600 dark:text-emerald-400 mb-2">{{ archiveResultMsg }}</p>

      <!-- mobile: cards -->
      <div class="sm:hidden space-y-2">
        <div v-for="b in boards" :key="b.slug" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-start gap-2">
            <input type="checkbox" :checked="selectedBoards.has(b.slug)" @change="toggleBoardSelect(b.slug)" class="mt-1 shrink-0" />
            <div class="min-w-0 flex-1">
              <RouterLink :to="`/boards/${b.slug}`" class="font-medium text-sm hover:underline">{{ b.title }}</RouterLink>
              <div class="text-[11px] text-slate-400 font-mono">{{ b.slug }}</div>
            </div>
            <button @click="deleteBoard(b)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline shrink-0">Delete</button>
          </div>
          <div class="text-[11px] text-slate-400 mt-1.5">{{ b.ownerName }} ({{ b.ownerEmail }})</div>
          <div class="text-[11px] text-slate-400 mt-0.5">
            {{ b.memberCount }} student(s) · {{ b.problemCount }} problem(s) · created {{ new Date(b.createdAt).toLocaleDateString() }}
          </div>
        </div>
        <p v-if="boards && !boards.length" class="text-slate-400 dark:text-slate-500 text-sm">No boards.</p>
      </div>

      <!-- desktop: table -->
      <div class="hidden sm:block overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">
                <input type="checkbox" :checked="!!boards?.length && selectedBoards.size === boards.length"
                       @change="selectAllBoards($event.target.checked)" />
              </th>
              <th class="font-normal pr-3">Title</th><th class="font-normal pr-3">Owner</th>
              <th class="font-normal pr-3">Students</th><th class="font-normal pr-3">Problems</th>
              <th class="font-normal pr-3">Created</th><th class="font-normal pr-3"></th>
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
              <td><button @click="deleteBoard(b)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Delete</button></td>
            </tr>
            <tr v-if="boards && !boards.length"><td colspan="7" class="text-slate-400 dark:text-slate-500 py-3">No boards.</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Problems -->
    <section v-show="tab === 'problems'" class="space-y-4">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">Export</h2>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">
          Downloads every bank problem (all owners) with its test cases as a JSON file.
        </p>
        <button @click="exportProblems"
                class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
          Download problems.json
        </button>
      </div>

      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">Import</h2>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">
          Upserts by (owner email, title). Owner resolved by email, falling back to the first teacher.
        </p>
        <label class="flex items-center gap-2 text-xs mb-2">
          <input type="checkbox" v-model="replaceExisting" /> Replace existing (overwrite a problem with the same owner + title)
        </label>
        <input type="file" accept="application/json,.json" @change="importProblems" :disabled="importing"
               class="text-sm" />
        <p v-if="importing" class="text-xs text-slate-400 mt-2">Importing…</p>
        <div v-if="importResult" class="mt-3 text-sm">
          <p class="text-emerald-600 dark:text-emerald-400">
            Created {{ importResult.created }} · Updated {{ importResult.updated }} · Skipped {{ importResult.skipped }}
          </p>
          <ul v-if="importResult.errors?.length" class="mt-1 text-xs text-red-600 dark:text-red-400 list-disc pl-5">
            <li v-for="(e, i) in importResult.errors" :key="i">{{ e }}</li>
          </ul>
        </div>
      </div>
    </section>

    <!-- AI review: AI-generated bank problems held back until approved -->
    <section v-show="tab === 'review'">
      <div class="flex items-center gap-2 mb-3">
        <button @click="loadAiReview" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>
        <span class="text-xs text-slate-400 dark:text-slate-500">{{ aiReview?.length ?? 0 }} waiting</span>
      </div>
      <div v-for="row in aiReview" :key="row.slug" class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 mb-3">
        <div class="flex items-start justify-between gap-2 flex-wrap">
          <div>
            <div class="font-semibold text-sm">{{ row.title }}</div>
            <div class="text-[11px] text-slate-400 dark:text-slate-500">
              by {{ row.ownerName }} ({{ row.ownerEmail }}) · {{ row.level }} · {{ row.tags || 'no tags' }} ·
              {{ row.allowedLanguages ? row.allowedLanguages : 'any language' }} ·
              {{ row.tests.length }} tests ({{ row.tests.filter(t => t.isSample).length }} sample) ·
              generated {{ when(row.createdAt) }}
            </div>
          </div>
          <div class="flex items-center gap-2 shrink-0">
            <button @click="expandedReview = expandedReview === row.slug ? null : row.slug"
                    class="text-xs text-slate-500 dark:text-slate-400 hover:underline">
              {{ expandedReview === row.slug ? 'hide' : 'view' }} statement &amp; tests
            </button>
            <button @click="approveAiReview(row)" class="text-xs bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg px-3 py-1.5 font-medium">
              ✓ Approve &amp; share
            </button>
          </div>
        </div>

        <div v-if="expandedReview === row.slug" class="mt-3 space-y-3">
          <div class="bg-slate-50 dark:bg-slate-800/40 rounded-lg p-3 max-h-64 overflow-auto">
            <MarkdownBlock :text="row.statementMarkdown" />
          </div>
          <div class="grid sm:grid-cols-2 gap-2">
            <div v-for="(t, i) in row.tests" :key="i" class="text-xs border border-slate-100 dark:border-slate-800 rounded-lg p-2">
              <div class="text-slate-400 mb-1">{{ t.isSample ? 'sample' : 'hidden' }} #{{ i + 1 }}</div>
              <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto">stdin: {{ t.stdin }}</pre>
              <pre class="bg-slate-100 dark:bg-slate-800 rounded p-1.5 overflow-x-auto mt-1">expected: {{ t.expectedStdout }}</pre>
            </div>
          </div>
          <div class="flex items-center gap-2">
            <input v-model="rejectReasonDraft[row.slug]" placeholder="Reason (optional, goes in the audit log)"
                   class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
            <button @click="rejectAiReview(row)" class="text-xs bg-rose-600 hover:bg-rose-700 text-white rounded-lg px-3 py-1.5 font-medium">
              ✕ Reject (stays private)
            </button>
          </div>
        </div>
      </div>
      <p v-if="aiReview && !aiReview.length" class="text-slate-400 dark:text-slate-500 text-sm">Nothing waiting for review.</p>
    </section>

    <!-- Reports: analytics export + system status -->
    <section v-show="tab === 'reports'" class="space-y-4">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">Analytics export</h2>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-3">
          CSV, ready to open in a spreadsheet — also useful as evidence for an impact/grant report.
        </p>
        <div class="flex flex-wrap gap-2">
          <button @click="downloadFile('/api/admin-ui/analytics/topics.csv', 'topic-stats.csv')"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            Topic solve rates
          </button>
          <button @click="downloadFile('/api/admin-ui/analytics/users.csv', 'user-xp.csv')"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            User XP
          </button>
          <button @click="downloadFile('/api/admin-ui/analytics/engagement.csv', 'weekly-engagement.csv')"
                  class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            Weekly engagement
          </button>
        </div>
        <div class="mt-3 pt-3 border-t border-slate-100 dark:border-slate-800">
          <button @click="downloadFile('/api/admin-ui/analytics/report.xlsx', 'beecoding-report.xlsx')"
                  class="bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
            📊 Export all as Excel (.xlsx)
          </button>
          <p class="text-[11px] text-slate-400 dark:text-slate-500 mt-1">
            One workbook, three sheets — Topics, Users, Weekly engagement.
          </p>
        </div>
      </div>

      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <div class="flex items-center gap-2 mb-2">
          <h2 class="font-semibold text-sm">System status</h2>
          <button @click="loadSystemStatus" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>
        </div>
        <div v-if="systemStatus" class="grid sm:grid-cols-2 gap-x-6 gap-y-1.5 text-sm">
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Judge sandbox</span>
            <span :class="systemStatus.bwrapUsable ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'">
              {{ systemStatus.sandboxMode }}
            </span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Sandbox required (fail closed)</span>
            <span>{{ systemStatus.sandboxRequired ? 'yes' : 'no' }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Judge queue backend</span>
            <span>{{ systemStatus.judgeQueueBackend }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Pending judge jobs</span>
            <span>{{ systemStatus.pendingJudgeJobs ?? 'n/a' }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Realtime backend</span>
            <span>{{ systemStatus.realtimeBackend }}</span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Redis</span>
            <span v-if="!systemStatus.redisConfigured" class="text-slate-400">not used</span>
            <span v-else :class="systemStatus.redisConnected ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'">
              {{ systemStatus.redisConnected ? 'connected' : 'disconnected' }}
            </span>
          </div>
          <div class="flex justify-between border-b border-slate-100 dark:border-slate-800/60 py-1">
            <span class="text-slate-400 dark:text-slate-500">Database</span>
            <span :class="systemStatus.databaseOk ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'">
              {{ systemStatus.databaseOk ? 'reachable' : 'unreachable' }}
            </span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-slate-400 dark:text-slate-500">Checked</span>
            <span class="text-[11px] text-slate-400">{{ when(systemStatus.checkedAt) }}</span>
          </div>
        </div>
      </div>
    </section>

    <!-- Trash: soft-deleted rows. Restore has no time limit here; Purge is permanent. -->
    <section v-show="tab === 'trash'" class="space-y-5">
      <button @click="loadTrash" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>

      <div v-for="[kind, label, labelFn] in TRASH_KINDS" :key="kind">
        <div class="flex items-center gap-3 mb-1.5 flex-wrap">
          <h2 class="font-semibold text-sm">{{ label }} · {{ trash[kind].total }}</h2>
          <div v-if="trash[kind].selected.size" class="flex items-center gap-2 text-xs">
            <span>{{ trash[kind].selected.size }} selected</span>
            <button @click="restoreSelectedTrash(kind)" class="text-emerald-600 dark:text-emerald-400 hover:underline">Restore selected</button>
            <button @click="purgeSelectedTrash(kind)" class="text-rose-600 dark:text-rose-400 hover:underline">Purge selected</button>
          </div>
        </div>
        <p v-if="trash[kind].msg" class="text-[11px] text-emerald-600 dark:text-emerald-400 mb-1">{{ trash[kind].msg }}</p>

        <!-- mobile: cards -->
        <div class="sm:hidden space-y-1.5">
          <label v-if="trash[kind].rows?.length" class="flex items-center gap-2 text-[11px] text-slate-400 dark:text-slate-500 px-1">
            <input type="checkbox" :checked="trash[kind].selected.size === trash[kind].rows.length"
                   @change="selectAllTrash(kind, $event.target.checked)" /> Select all
          </label>
          <div v-for="row in trash[kind].rows" :key="trashKey(kind, row)" class="border border-slate-200 dark:border-slate-800 rounded-lg p-2.5">
            <div class="flex items-start gap-2">
              <input type="checkbox" :checked="trash[kind].selected.has(trashKey(kind, row))" @change="toggleTrashSelect(kind, row)" class="mt-1 shrink-0" />
              <div class="min-w-0 flex-1">
                <div class="font-medium text-sm truncate">{{ labelFn(row) }}</div>
                <div class="text-[11px] text-slate-400">{{ row.email || row.ownerEmail || '' }} · deleted {{ when(row.deletedAt) }}</div>
              </div>
            </div>
            <div class="mt-1.5 flex gap-3">
              <button @click="restoreTrash(kind, row)" class="text-[11px] text-emerald-600 dark:text-emerald-400 hover:underline">Restore</button>
              <button @click="purge(kind, row, labelFn(row))" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Purge</button>
            </div>
          </div>
          <p v-if="trash[kind].rows && !trash[kind].rows.length" class="text-slate-400 dark:text-slate-500 text-xs px-1">Empty.</p>
        </div>

        <!-- desktop: table -->
        <div class="hidden sm:block overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
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
                <td class="text-[11px] text-slate-400">deleted {{ when(row.deletedAt) }}</td>
                <td class="text-right">
                  <button @click="restoreTrash(kind, row)" class="text-[11px] text-emerald-600 dark:text-emerald-400 hover:underline mr-3">Restore</button>
                  <button @click="purge(kind, row, labelFn(row))" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Purge</button>
                </td>
              </tr>
              <tr v-if="trash[kind].rows && !trash[kind].rows.length"><td colspan="5" class="text-slate-400 dark:text-slate-500 py-1.5 text-xs">Empty.</td></tr>
            </tbody>
          </table>
        </div>
        <div v-if="trash[kind].total > trash[kind].pageSize" class="flex items-center gap-3 mt-1.5 text-xs">
          <span class="text-slate-400 dark:text-slate-500">
            {{ (trash[kind].page - 1) * trash[kind].pageSize + 1 }}–{{ Math.min(trash[kind].page * trash[kind].pageSize, trash[kind].total) }} of {{ trash[kind].total }}
          </span>
          <div class="ml-auto flex gap-2">
            <button @click="trashPrevPage(kind)" :disabled="trash[kind].page === 1"
                    class="px-2 py-0.5 rounded border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
            <button @click="trashNextPage(kind)" :disabled="trash[kind].page * trash[kind].pageSize >= trash[kind].total"
                    class="px-2 py-0.5 rounded border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
          </div>
        </div>
      </div>
      <p v-if="TRASH_KINDS.every(([kind]) => trash[kind].rows === null)" class="text-slate-400 dark:text-slate-500 text-sm">Loading…</p>
    </section>

    <!-- Audit log -->
    <section v-show="tab === 'audit'">
      <div class="flex flex-wrap gap-2 mb-3">
        <input v-model="auditQ" @keyup.enter="searchAudit" placeholder="Search actor, action, or target…"
               class="flex-1 min-w-0 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="searchAudit" class="text-sm bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4">Search</button>
        <button @click="loadAudit" class="text-xs text-slate-500 dark:text-slate-400">↻ refresh</button>
      </div>

      <!-- mobile: cards -->
      <div class="sm:hidden space-y-2">
        <div v-for="r in auditRows" :key="r.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-center justify-between gap-2 mb-1">
            <span class="text-[11px] px-1.5 py-0.5 rounded-full whitespace-nowrap" :class="actionBadgeClass(r.action)">{{ r.action }}</span>
            <span class="text-[11px] text-slate-400 whitespace-nowrap">{{ when(r.createdAt) }}</span>
          </div>
          <div class="text-xs">{{ r.actorEmail }}</div>
          <div class="text-[11px] text-slate-400 mt-0.5">{{ r.targetType }} · {{ r.targetLabel }}</div>
        </div>
        <p v-if="auditRows && !auditRows.length" class="text-slate-400 dark:text-slate-500 text-sm">No activity yet.</p>
      </div>

      <!-- desktop: table -->
      <div class="hidden sm:block overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">When</th><th class="font-normal pr-3">Actor</th>
              <th class="font-normal pr-3">Action</th><th class="font-normal pr-3">Target</th>
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
            <tr v-if="auditRows && !auditRows.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No activity yet.</td></tr>
          </tbody>
        </table>
      </div>
      <div v-if="auditTotal" class="flex items-center gap-3 mt-3 text-sm">
        <span class="text-slate-400 dark:text-slate-500">
          {{ (auditPage - 1) * auditPageSize + 1 }}–{{ Math.min(auditPage * auditPageSize, auditTotal) }} of {{ auditTotal }}
        </span>
        <div class="ml-auto flex gap-2">
          <button @click="auditPrevPage" :disabled="auditPage === 1"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Prev</button>
          <button @click="auditNextPage" :disabled="auditPage * auditPageSize >= auditTotal"
                  class="px-3 py-1 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40">Next</button>
        </div>
      </div>
    </section>

    <!-- Organizations: multi-tenant (separate universities/institutions) -->
    <section v-show="tab === 'organizations'" class="space-y-5">
      <p class="text-xs text-slate-400 dark:text-slate-500">
        Each organization gets its own boards, members, and AI quota — manage members/boards
        for one via <RouterLink to="/org-admin" class="underline">Organization</RouterLink>
        (this page only creates/removes the organization itself).
      </p>
      <div class="flex flex-col sm:flex-row gap-2">
        <input v-model="orgForm.name" @keyup.enter="createOrganization" placeholder="Name (e.g. BINUS University)"
               class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <input v-model="orgForm.slug" @keyup.enter="createOrganization" placeholder="slug (e.g. binus)"
               class="sm:w-40 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <button @click="createOrganization" class="text-sm bg-amber-500 text-white rounded-lg px-4 py-2 sm:py-0 font-medium">Add</button>
      </div>

      <!-- mobile: cards -->
      <div class="sm:hidden space-y-2">
        <div v-for="o in organizations" :key="o.id" class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
          <div class="flex items-start justify-between gap-2">
            <div class="font-medium text-sm">{{ o.name }}</div>
            <button @click="deleteOrganization(o)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline shrink-0">Delete</button>
          </div>
          <div class="text-[11px] text-slate-400 mt-0.5">{{ o.slug }} · created {{ new Date(o.createdAt).toLocaleDateString() }}</div>
        </div>
        <p v-if="organizations && !organizations.length" class="text-slate-400 dark:text-slate-500 text-sm">No organizations yet.</p>
      </div>

      <!-- desktop: table -->
      <div class="hidden sm:block overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
              <th class="font-normal py-1.5 pr-3">Name</th><th class="font-normal pr-3">Slug</th>
              <th class="font-normal pr-3">Created</th><th class="font-normal pr-3"></th>
            </tr>
          </thead>
          <tbody class="[&_td]:py-1.5 [&_td]:pr-3">
            <tr v-for="o in organizations" :key="o.id" class="border-b border-slate-100 dark:border-slate-800/60">
              <td class="font-medium">{{ o.name }}</td>
              <td class="text-[11px] text-slate-400">{{ o.slug }}</td>
              <td class="text-[11px] text-slate-400">{{ new Date(o.createdAt).toLocaleDateString() }}</td>
              <td><button @click="deleteOrganization(o)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Delete</button></td>
            </tr>
            <tr v-if="organizations && !organizations.length"><td colspan="4" class="text-slate-400 dark:text-slate-500 py-3">No organizations yet.</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- LTI 1.3: register LMS platforms as tool consumers -->
    <section v-show="tab === 'lti'" class="space-y-5">
      <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold text-sm mb-1">Tool configuration</h2>
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-3">
          Give these to whoever registers BeeCoding as an external tool in the LMS. The
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
          <input v-model="ltiForm.name" placeholder="Name (e.g. BINUS Moodle)" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.issuer" placeholder="Issuer (iss)" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.clientId" placeholder="Client ID" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.deploymentIds" placeholder="Deployment ID(s), comma-separated" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.authLoginUrl" placeholder="Auth login URL" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.authTokenUrl" placeholder="Auth token URL" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <input v-model="ltiForm.jwksUrl" placeholder="JWKS (key set) URL" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5" />
          <select v-model="ltiForm.organizationId" class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-1.5">
            <option :value="null">No organization (unaffiliated)</option>
            <option v-for="o in organizations" :key="o.id" :value="o.id">{{ o.name }}</option>
          </select>
          <p class="text-[11px] text-slate-400 dark:text-slate-500 -mt-1">
            Launches through this platform auto-join the organization and auto-assign new boards to it.
          </p>
          <label class="flex items-center gap-2 text-xs"><input type="checkbox" v-model="ltiForm.enabled" /> Enabled</label>
          <div class="flex gap-2 pt-1">
            <button @click="saveLtiPlatform" class="bg-amber-500 text-white rounded-lg px-4 py-1.5 text-sm font-medium">Save</button>
            <button @click="cancelLtiEdit" class="text-slate-500 dark:text-slate-400 px-3 text-sm">Cancel</button>
          </div>
        </div>

        <!-- mobile: cards -->
        <div class="sm:hidden space-y-2">
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
            <div class="text-[11px] text-slate-400 mt-0.5">{{ p.organizationName || 'unaffiliated' }}</div>
            <div class="mt-2 flex gap-3">
              <button @click="startEditLtiPlatform(p)" class="text-[11px] text-violet-600 dark:text-violet-400 hover:underline">Edit</button>
              <button @click="deleteLtiPlatform(p)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Remove</button>
            </div>
          </div>
          <p v-if="ltiPlatforms && !ltiPlatforms.length" class="text-slate-400 dark:text-slate-500 text-sm">No platforms registered yet.</p>
        </div>

        <!-- desktop: table -->
        <div class="hidden sm:block overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs text-left text-slate-400 dark:text-slate-500 border-b border-slate-200 dark:border-slate-800">
                <th class="font-normal py-1.5 pr-3">Name</th><th class="font-normal pr-3">Issuer</th>
                <th class="font-normal pr-3">Client ID</th><th class="font-normal pr-3">Organization</th>
                <th class="font-normal pr-3">Status</th><th class="font-normal pr-3"></th>
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
                    {{ p.enabled ? 'enabled' : 'disabled' }}
                  </span>
                </td>
                <td class="whitespace-nowrap">
                  <button @click="startEditLtiPlatform(p)" class="text-[11px] text-violet-600 dark:text-violet-400 hover:underline mr-3">Edit</button>
                  <button @click="deleteLtiPlatform(p)" class="text-[11px] text-rose-600 dark:text-rose-400 hover:underline">Remove</button>
                </td>
              </tr>
              <tr v-if="ltiPlatforms && !ltiPlatforms.length"><td colspan="6" class="text-slate-400 dark:text-slate-500 py-3">No platforms registered yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>
  </div>
</template>
