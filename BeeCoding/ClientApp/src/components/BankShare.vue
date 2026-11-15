<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';

// Owner only: let other teachers read this private problem and copy it to their own boards (read-only, no publish).
const props = defineProps({ slug: { type: String, required: true } });
const shares = ref([]);
const email = ref('');
const error = ref('');
const busy = ref(false);

async function load() {
  try { shares.value = await api.get(`/api/bank/${props.slug}/shares`); } catch (e) { error.value = e.message; }
}
async function add() {
  if (!email.value.trim()) return;
  error.value = ''; busy.value = true;
  try {
    await api.post(`/api/bank/${props.slug}/shares`, { email: email.value.trim() });
    email.value = '';
    await load();
  } catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}
async function remove(s) {
  error.value = '';
  try { await api.del(`/api/bank/${props.slug}/shares/${s.userId}`); await load(); } catch (e) { error.value = e.message; }
}
onMounted(load);
</script>

<template>
  <section class="max-w-7xl mx-auto px-4 pb-10">
    <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 max-w-xl">
      <h2 class="font-semibold text-sm mb-1">Share with teachers</h2>
      <p class="text-xs text-slate-500 dark:text-slate-400 mb-3">
        A teacher you add can read this problem, with all its tests, and copy it to their own boards. They cannot edit, delete or publish it.
        Stopping the share removes their access, but a copy they already made stays on their board.
      </p>
      <div class="flex gap-2">
        <input v-model="email" @keyup.enter="add" type="email" placeholder="teacher@school.example" aria-label="Teacher email"
               class="flex-1 min-w-0 border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5 text-sm" />
        <button @click="add" :disabled="busy || !email.trim()"
                class="bg-slate-800 dark:bg-slate-700 text-white rounded-lg px-4 py-1.5 text-sm font-medium disabled:opacity-50">Share</button>
      </div>
      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400 mt-2">{{ error }}</p>
      <ul v-if="shares.length" class="mt-3 divide-y divide-slate-100 dark:divide-slate-800 text-sm">
        <li v-for="s in shares" :key="s.userId" class="flex items-center gap-2 py-1.5">
          <span class="truncate">{{ s.displayName }} <span class="text-xs text-slate-400">{{ s.email }}</span></span>
          <button @click="remove(s)" class="row-action-btn row-action-btn--danger ml-auto"><span>✖️</span><span>Stop sharing</span></button>
        </li>
      </ul>
    </div>
  </section>
</template>
