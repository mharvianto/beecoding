<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useConfirmDialog } from '../stores/confirmDialog';

// Organizations this account belongs to, invitations to join one, and leaving. Joining is never silent: an org admin
// can see a member's practice activity (verdicts per problem, not code), so the user answers each invitation.
const auth = useAuth();
const confirmDialog = useConfirmDialog();
const invites = ref([]);
const orgs = ref([]);
const error = ref('');
const busy = ref(false);

async function load() {
  try {
    [invites.value, orgs.value] = await Promise.all([api.get('/api/org-invites'), api.get('/api/org-invites/memberships')]);
  } catch (e) { error.value = e.message; }
}
onMounted(load);

async function run(fn) {
  error.value = ''; busy.value = true;
  try { await fn(); await load(); await auth.fetchMe(); } catch (e) { error.value = e.message; } finally { busy.value = false; }
}
const accept = (i) => run(() => api.post(`/api/org-invites/${i.id}/accept`));
const decline = (i) => run(() => api.post(`/api/org-invites/${i.id}/decline`));
async function leave(o) {
  if (!(await confirmDialog.ask(`Leave "${o.name}"? Its admins will no longer see your practice activity.`, { confirmLabel: 'Leave' }))) return;
  await run(() => api.del(`/api/org-invites/memberships/${o.organizationId}`));
}
</script>

<template>
  <section v-if="invites.length || orgs.length || error" id="organizations" class="space-y-3 scroll-mt-4">
    <h2 class="font-semibold text-sm">Organizations</h2>
    <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>

    <div v-for="i in invites" :key="i.id" class="rounded-xl border border-amber-200 dark:border-amber-500/30 bg-amber-50/60 dark:bg-amber-500/5 p-3 space-y-2">
      <p class="text-sm"><b>{{ i.invitedBy }}</b> invited you to join <b>{{ i.organizationName }}</b> as {{ i.orgRole.toLowerCase() }}.</p>
      <p class="text-xs text-slate-500 dark:text-slate-400">
        If you join, that organization's admins can see which practice problems you attempted and with what result (not your code), and you appear in its leaderboard.
        You can leave at any time.
      </p>
      <div class="flex gap-2">
        <button @click="accept(i)" :disabled="busy" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">Join</button>
        <button @click="decline(i)" :disabled="busy" class="text-sm text-slate-500 dark:text-slate-400 px-2">Decline</button>
      </div>
    </div>

    <ul v-if="orgs.length" class="divide-y divide-slate-100 dark:divide-slate-800 border border-slate-200 dark:border-slate-800 rounded-xl px-3">
      <li v-for="o in orgs" :key="o.organizationId" class="py-2 flex items-center gap-2">
        <span class="text-sm flex-1 min-w-0 truncate">{{ o.name }} <span class="text-xs text-slate-400">· {{ o.orgRole.toLowerCase() }}</span></span>
        <button @click="leave(o)" :disabled="busy" class="row-action-btn row-action-btn--danger">Leave</button>
      </li>
    </ul>
  </section>
</template>
