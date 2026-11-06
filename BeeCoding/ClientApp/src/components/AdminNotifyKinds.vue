<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';

// Platform-wide: switch a kind of notification off for everyone, or allow it to be emailed (each person still opts in).
const s = ref(null);
const err = ref('');
const msg = ref('');
const saving = ref(false);

onMounted(async () => { try { s.value = await api.get('/api/admin-ui/notification-kinds'); } catch (e) { err.value = e.message; } });

async function save() {
  saving.value = true; err.value = ''; msg.value = '';
  try {
    s.value = await api.put('/api/admin-ui/notification-kinds', s.value.kinds);
    msg.value = 'Saved.';
  } catch (e) { err.value = e.message; } finally { saving.value = false; }
}
</script>

<template>
  <section v-if="s" class="rounded-xl border border-slate-200 dark:border-slate-800 px-4 pt-3 pb-3">
    <h4 class="text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Notification kinds</h4>
    <p class="mt-1 text-[11px] leading-snug text-slate-400 dark:text-slate-500">
      “Locked” = everyone gets it in the bell and cannot mute it. “On” = created at all (off: nobody gets it, not in the bell, push or email). “Email” = people may choose to get it by email
      <template v-if="!s.emailConfigured">— needs the <code>Email</code> settings (SMTP) in appsettings, so it has no effect yet</template>.
      Email goes only to verified addresses and only to people who turned it on in their account.
    </p>
    <div class="mt-2 divide-y divide-slate-100 dark:divide-slate-800">
      <div v-for="k in s.kinds" :key="k.kind" class="flex items-center gap-3 py-2">
        <span class="flex-1 min-w-0 text-sm text-slate-700 dark:text-slate-200">{{ k.label }}</span>
        <label class="flex items-center gap-1 text-xs text-slate-500"><input type="checkbox" v-model="k.enabled" /> On</label>
        <label class="flex items-center gap-1 text-xs text-slate-500"><input type="checkbox" v-model="k.email" :disabled="!k.enabled" /> Email</label>
        <label class="flex items-center gap-1 text-xs text-slate-500"><input type="checkbox" v-model="k.locked" :disabled="!k.enabled" /> Locked</label>
      </div>
    </div>
    <div class="mt-3 flex items-center gap-3">
      <button @click="save" :disabled="saving" class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">{{ saving ? 'Saving…' : 'Save' }}</button>
      <span v-if="msg" class="text-sm text-emerald-600 dark:text-emerald-400">{{ msg }}</span>
      <span v-if="err" class="text-sm text-red-600 dark:text-red-400">{{ err }}</span>
    </div>
  </section>
</template>
