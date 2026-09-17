<script setup>
import { ref, onMounted, computed } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';

const auth = useAuth();
const boards = ref([]);
const newTitle = ref('');
const joinCode = ref('');
const error = ref('');
const q = ref('');

async function load() {
  boards.value = await api.get('/api/boards');
}
onMounted(load);

const filteredBoards = computed(() => {
  const needle = q.value.trim().toLowerCase();
  if (!needle) return boards.value;
  return boards.value.filter((b) =>
    b.title.toLowerCase().includes(needle) || (b.tags || '').toLowerCase().includes(needle));
});

async function createBoard() {
  error.value = '';
  try {
    await api.post('/api/boards', { title: newTitle.value });
    newTitle.value = '';
    await load();
  } catch (e) { error.value = e.message; }
}

async function join() {
  error.value = '';
  try {
    await api.post('/api/boards/join', { code: joinCode.value });
    joinCode.value = '';
    await load();
  } catch (e) { error.value = e.message; }
}
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-8">
    <h1 class="text-xl font-bold mb-1">Your boards</h1>
    <p class="text-sm text-slate-400 dark:text-slate-500 mb-6">
      A board is shared with a class via a join code — for a class you're not in yet, ask the
      teacher for their code.
      <template v-if="auth.isTeacher">Building a personal problem library instead? See
        <RouterLink to="/bank" class="text-amber-600 dark:text-amber-400 hover:underline">Problem bank</RouterLink>.
      </template>
      Just want to solve something for XP right now? Try
      <RouterLink to="/practice" class="text-amber-600 dark:text-amber-400 hover:underline">Practice</RouterLink>.
    </p>

    <div class="grid sm:grid-cols-2 gap-4 mb-8">
      <div v-if="auth.isTeacher" class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold mb-2 text-sm text-slate-600 dark:text-slate-300">Create a board</h2>
        <form @submit.prevent="createBoard" class="flex gap-2">
          <input v-model="newTitle" placeholder="Board title" required
                 class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
          <button class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 font-medium">Create</button>
        </form>
      </div>
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4">
        <h2 class="font-semibold mb-2 text-sm text-slate-600 dark:text-slate-300">Join a board</h2>
        <form @submit.prevent="join" class="flex gap-2">
          <input v-model="joinCode" placeholder="Join code" required
                 class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 uppercase" />
          <button class="bg-slate-800 hover:bg-slate-900 dark:bg-slate-700 dark:hover:bg-slate-600 text-white rounded-lg px-4 font-medium">Join</button>
        </form>
      </div>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-4">{{ error }}</p>

    <input v-if="boards.length > 1" v-model="q" placeholder="Search by title or tag…"
           class="w-full sm:w-80 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm mb-4" />

    <div class="grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
      <RouterLink v-for="b in filteredBoards" :key="b.id" :to="`/boards/${b.slug}`"
                  class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-amber-400 dark:hover:border-amber-500 transition">
        <div class="flex items-center justify-between">
          <h3 class="font-semibold">{{ b.title }}</h3>
          <span class="text-xs px-2 py-0.5 rounded-full"
                :class="b.role === 'Student'
                  ? 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300'
                  : 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'">
            {{ b.role }}
          </span>
        </div>
        <div v-if="b.tags" class="flex flex-wrap gap-1 mt-1.5">
          <span v-for="t in b.tags.split(',')" :key="t"
                class="text-[10px] px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ t }}</span>
        </div>
        <div class="text-sm text-slate-500 dark:text-slate-400 mt-2 flex gap-4">
          <span>{{ b.problemCount }} problem{{ b.problemCount === 1 ? '' : 's' }}</span>
          <span>{{ b.memberCount }} student{{ b.memberCount === 1 ? '' : 's' }}</span>
        </div>
        <div v-if="b.role !== 'Student'" class="text-xs text-slate-400 dark:text-slate-500 mt-2">
          Code: <span class="font-mono font-semibold text-slate-600 dark:text-slate-300">{{ b.joinCode }}</span>
        </div>
      </RouterLink>
    </div>
    <p v-if="boards.length && !filteredBoards.length" class="text-slate-400 dark:text-slate-500 text-sm">
      No boards match "{{ q }}".
    </p>
    <div v-if="!boards.length" class="border border-dashed border-slate-300 dark:border-slate-700 rounded-xl p-6 text-center">
      <p class="text-slate-500 dark:text-slate-400 text-sm">
        {{ auth.isTeacher ? "You don't own or belong to any board yet." : "You haven't joined a board yet." }}
      </p>
      <p class="text-slate-400 dark:text-slate-500 text-sm mt-1">
        <template v-if="auth.isTeacher">
          Create one above to start a class, or
          <RouterLink to="/bank" class="text-amber-600 dark:text-amber-400 hover:underline">build a problem bank</RouterLink>
          first so you have problems ready to add.
        </template>
        <template v-else>
          Ask your teacher for a join code, or
          <RouterLink to="/practice" class="text-amber-600 dark:text-amber-400 hover:underline">start practicing for XP</RouterLink>
          on your own in the meantime.
        </template>
      </p>
    </div>
  </div>
</template>
