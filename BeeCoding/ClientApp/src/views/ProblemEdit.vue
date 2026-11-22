<script setup>
import { tr } from '../lib/i18n';
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import { useRouter, onBeforeRouteLeave } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useUndoToast } from '../stores/undoToast';
import { useConfirmDialog } from '../stores/confirmDialog';
import ProblemEditor from '../components/ProblemEditor.vue';
import BankShare from '../components/BankShare.vue';

const undoToast = useUndoToast();
const confirmDialog = useConfirmDialog();
const auth = useAuth();

// One page for all four routes:
//   /bank/new  /bank/:problemSlug/edit
//   /boards/:slug/problems/new  /boards/:slug/problems/:problemSlug/edit
const props = defineProps({ problemSlug: String, slug: String });
const router = useRouter();

const isBoard = computed(() => !!props.slug);
const editSlug = computed(() => props.problemSlug || null);
const listPath = computed(() => (isBoard.value ? `/boards/${props.slug}` : '/bank'));
const apiBase = computed(() => (isBoard.value ? `/api/boards/${props.slug}/problems` : '/api/bank'));

const problem = ref(null);        // null = new / not loaded yet
const loaded = ref(false);
const error = ref('');
const busy = ref(false);
const aiEnabled = ref(false);
const regenBusy = ref(false);
const addExtremeBusy = ref(false);

// Unsaved edits: ask before the tab is closed/reloaded (browser's own prompt) or the route is left (back button, links, Cancel).
const dirty = ref(false);
const warnUnload = (e) => { if (dirty.value) { e.preventDefault(); e.returnValue = ''; } };
window.addEventListener('beforeunload', warnUnload);
onBeforeUnmount(() => window.removeEventListener('beforeunload', warnUnload));
onBeforeRouteLeave(() => !dirty.value
  || confirmDialog.ask(tr('You have unsaved changes to this problem. Leave without saving?'), { confirmLabel: tr('Leave') }));

async function load() {
  error.value = '';
  try {
    problem.value = editSlug.value ? await api.get(`${apiBase.value}/${editSlug.value}`) : null;
  } catch (e) { error.value = e.message; }
  loaded.value = true;
}

async function save(form) {
  error.value = ''; busy.value = true;
  try {
    const body = { ...(form.value ?? form) };
    if (editSlug.value) await api.put(`${apiBase.value}/${editSlug.value}`, body);
    else await api.post(apiBase.value, body);
    dirty.value = false;
    router.push(listPath.value);
  } catch (e) {
    // Locked after the first submission: offer to put these edits on a new (hidden) version instead of losing them.
    if (e.status === 409 && isBoard.value && editSlug.value
        && await confirmDialog.ask(`${e.message}\n\nSave your changes as a new version? It is created as a hidden draft; the current problem is left as it is.`, { confirmLabel: tr('Create new version') })) {
      try {
        const body = { ...(form.value ?? form) };
        const v2 = await api.post(`${apiBase.value}/${editSlug.value}/new-version`);
        await api.put(`${apiBase.value}/${v2.slug}`, body);
        dirty.value = false;
        router.push(listPath.value);
      } catch (e2) { error.value = e2.message; }
    } else error.value = e.message;
  }
  finally { busy.value = false; }
}

async function remove() {
  if (!editSlug.value) return;
  const slug = editSlug.value;
  const base = apiBase.value;
  const title = problem.value?.title || 'Problem';
  error.value = '';
  try {
    await api.del(`${base}/${slug}`);
    dirty.value = false;
    router.push(listPath.value);
    undoToast.show(`"${title}" deleted.`, async () => {
      await api.post(`${base}/${slug}/restore`);
      // Undo can fire well after we've already navigated to the list page (a different
      // component instance whose own list we have no handle on) — a full reload is the
      // simplest way to guarantee the restored problem actually reappears.
      window.location.reload();
    });
  } catch (e) { error.value = e.message; }
}

function cancel() {
  if (window.history.length > 1) router.back();
  else router.push(listPath.value);
}

// Both AI test actions work on the saved problem and then replace the form with the result.
const discardEditsOk = () => !dirty.value
  || confirmDialog.ask(tr('This replaces the form with the saved problem plus the new tests, so your unsaved changes will be lost. Continue?'), { confirmLabel: tr('Continue') });

// Both AI test actions are server jobs that change the saved problem even if this page is closed. The job id is kept
// (30 min = the server's job lifetime) so coming back resumes the wait and shows the result.
const jobKey = (kind) => `beecoding.aijob.${kind}.${problem.value?.id}`;
const keepJob = (kind, jobId) => { try { jobId ? localStorage.setItem(jobKey(kind), JSON.stringify({ jobId, at: Date.now() })) : localStorage.removeItem(jobKey(kind)); } catch { /* ignore */ } };

async function pollTestsJob(kind, jobId, failText) {
  for (let n = 0; n < 240; n++) {
    await new Promise((r) => setTimeout(r, 2000));
    let r;
    try { r = await api.get(`/api/ai/generate-problem/${jobId}`); }
    catch (e) { if (e.status === 404) { keepJob(kind, null); error.value = tr('The job expired.'); return; } continue; }
    if (r.status === 'running') continue;
    keepJob(kind, null);
    if (r.status === 'done') problem.value = r.problem;
    else error.value = (r.message || failText) + (r.compilerOutput ? '\n\n' + r.compilerOutput : '') + (r.stderr ? '\n\n' + r.stderr : '');
    return;
  }
}

// regenerate hidden tests / append extreme tests — bank problems only
async function runTestsJob(kind, busy, failText) {
  if (isBoard.value || !problem.value?.id) return;
  if (!(await discardEditsOk())) return;
  error.value = ''; busy.value = true;
  try {
    const { jobId } = await api.post(`/api/ai/${kind === 'regen' ? 'regenerate-tests' : 'add-extreme-tests'}/${problem.value.id}`);
    keepJob(kind, jobId);
    await pollTestsJob(kind, jobId, failText);
  } catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}
const regenerateTests = () => runTestsJob('regen', regenBusy, tr('Regeneration failed.'));
const addExtremeTests = () => runTestsJob('extreme', addExtremeBusy, tr('Adding extreme tests failed.'));

async function resumeTestsJobs() {
  for (const [kind, busy, failText] of [['regen', regenBusy, tr('Regeneration failed.')], ['extreme', addExtremeBusy, tr('Adding extreme tests failed.')]]) {
    let saved = null;
    try { saved = JSON.parse(localStorage.getItem(jobKey(kind)) || 'null'); } catch { /* ignore */ }
    if (!saved?.jobId || Date.now() - saved.at > 30 * 60_000) { keepJob(kind, null); continue; }
    busy.value = true;
    try { await pollTestsJob(kind, saved.jobId, failText); } finally { busy.value = false; }
  }
}

onMounted(async () => {
  await load();
  if (!isBoard.value) {
    try { aiEnabled.value = (await api.get('/api/ai/enabled'))?.enabled === true; } catch { /* ignore */ }
    if (aiEnabled.value && problem.value?.id) resumeTestsJobs();
  }
});
</script>

<template>
  <div>
    <p v-if="!loaded" class="max-w-7xl mx-auto px-4 py-10 text-sm text-slate-500 dark:text-slate-400">{{ tr('Loading…') }}</p>
    <ProblemEditor v-else
      :problem="problem"
      :show-bank-fields="!isBoard"
      :student-mode="!isBoard && !auth.isTeacher"
      :error="error"
      :busy="busy"
      :can-regen-tests="!isBoard && auth.isTeacher && aiEnabled && !!editSlug"
      :regen-busy="regenBusy"
      :add-extreme-busy="addExtremeBusy"
      @dirty="dirty = $event" @save="save" @delete="remove" @cancel="cancel" @regenerate-tests="regenerateTests"
      @add-extreme-tests="addExtremeTests" />
    <BankShare v-if="loaded && !isBoard && auth.isTeacher && editSlug && problem?.mine" :slug="editSlug" />
  </div>
</template>
