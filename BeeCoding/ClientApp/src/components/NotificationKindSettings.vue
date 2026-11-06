<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';

// What the user wants to be told about: per kind, show it in the bell and/or also email it. Saved on every change.
const s = ref(null);
const err = ref('');
const saving = ref(false);

onMounted(async () => { try { s.value = await api.get('/api/notifications/settings'); } catch (e) { err.value = e.message; } });

async function save() {
  saving.value = true; err.value = '';
  try {
    s.value = await api.put('/api/notifications/settings', s.value.kinds.map((k) => ({ kind: k.kind, bell: k.bell, email: k.email })));
  } catch (e) { err.value = e.message; } finally { saving.value = false; }
}
const canEmail = (k) => s.value.emailAvailable && s.value.emailVerified && k.emailAllowed;
</script>

<template>
  <section v-if="s" class="space-y-3">
    <h2 class="font-semibold text-sm">What to notify me about</h2>
    <p class="text-sm text-slate-500 dark:text-slate-400">
      Choose the kinds of notification you get in the bell (and as desktop or push notifications).
      <template v-if="s.emailAvailable">Email is optional and sent only for kinds you also keep in the bell.</template>
    </p>
    <p v-if="s.emailAvailable && !s.emailVerified" class="text-xs text-amber-600 dark:text-amber-400">Verify your email address to receive these by email.</p>
    <div class="rounded-xl border border-slate-200 dark:border-slate-800 divide-y divide-slate-100 dark:divide-slate-800">
      <div class="flex items-center gap-3 px-4 py-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">
        <span class="flex-1">Kind</span><span class="w-12 text-center">Bell</span><span v-if="s.emailAvailable" class="w-12 text-center">Email</span>
      </div>
      <div v-for="k in s.kinds" :key="k.kind" class="flex items-center gap-3 px-4 py-2.5">
        <span class="flex-1 min-w-0 text-sm text-slate-700 dark:text-slate-200">
          {{ k.label }}
          <span v-if="k.platformOff" class="block text-[11px] text-slate-400 dark:text-slate-500">Turned off for everyone by an admin.</span>
          <span v-else-if="k.locked" class="block text-[11px] text-slate-400 dark:text-slate-500">Required by an admin: always in your bell.</span>
        </span>
        <span class="w-12 text-center"><input type="checkbox" v-model="k.bell" :disabled="k.platformOff || k.locked || saving" @change="save" :aria-label="`${k.label}: bell`" /></span>
        <span v-if="s.emailAvailable" class="w-12 text-center">
          <input type="checkbox" v-model="k.email" :disabled="k.platformOff || !k.bell || !canEmail(k) || saving" @change="save" :aria-label="`${k.label}: email`" />
        </span>
      </div>
    </div>
    <p v-if="err" class="text-sm text-red-600 dark:text-red-400">{{ err }}</p>
  </section>
</template>
