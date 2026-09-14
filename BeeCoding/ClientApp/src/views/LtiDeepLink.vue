<script setup>
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '../lib/api';

const route = useRoute();
const token = route.query.token || '';

const context = ref(null);
const boards = ref(null);
const error = ref('');
const selecting = ref(false);
const newTitle = ref('');
const creating = ref(false);

const mode = ref('board');   // 'board' | 'problem'
const problems = ref(null);
const problemQ = ref('');
const problemScope = ref('mine');   // 'mine' | 'public' | 'all'

async function load() {
  error.value = '';
  try {
    context.value = await api.get(`/lti/deep-link/context?token=${encodeURIComponent(token)}`);
    boards.value = (await api.get('/api/boards')).filter((b) => b.isOwner);
  } catch (e) { error.value = e.message; }
}
onMounted(load);

async function loadProblems() {
  error.value = '';
  try {
    const p = new URLSearchParams({ scope: problemScope.value });
    if (problemQ.value.trim()) p.set('q', problemQ.value.trim());
    problems.value = await api.get(`/api/bank?${p}`);
  } catch (e) { error.value = e.message; }
}
function setMode(m) {
  mode.value = m;
  if (m === 'problem' && problems.value === null) loadProblems();
}

function submitToLms(returnUrl, jwt) {
  const form = document.createElement('form');
  form.method = 'POST';
  form.action = returnUrl;
  const input = document.createElement('input');
  input.type = 'hidden';
  input.name = 'JWT';
  input.value = jwt;
  form.appendChild(input);
  document.body.appendChild(form);
  form.submit();
}

async function selectBoard(slug) {
  error.value = ''; selecting.value = true;
  try {
    const { returnUrl, jwt } = await api.post('/lti/deep-link/select', { token, boardSlug: slug });
    submitToLms(returnUrl, jwt);
  } catch (e) { error.value = e.message; selecting.value = false; }
}

async function selectProblem(slug) {
  error.value = ''; selecting.value = true;
  try {
    const { returnUrl, jwt } = await api.post('/lti/deep-link/select', { token, bankProblemSlug: slug });
    submitToLms(returnUrl, jwt);
  } catch (e) { error.value = e.message; selecting.value = false; }
}

async function createAndSelect() {
  if (!newTitle.value.trim()) return;
  error.value = ''; creating.value = true;
  try {
    const board = await api.post('/api/boards', { title: newTitle.value.trim() });
    await selectBoard(board.slug);
  } catch (e) { error.value = e.message; creating.value = false; }
}
</script>

<template>
  <div class="max-w-lg mx-auto px-4 py-10">
    <h1 class="text-xl font-bold mb-1">Add a BeeCoding activity</h1>
    <p v-if="context" class="text-sm text-slate-400 dark:text-slate-500 mb-5">
      Picking a board or problem here links it to this activity in {{ context.platformName }} —
      everyone who opens it from the course lands on the same one.
    </p>

    <div class="flex gap-1 text-sm mb-5">
      <button @click="setMode('board')" class="rounded-lg px-3 py-1.5"
              :class="mode === 'board' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
        Board
      </button>
      <button @click="setMode('problem')" class="rounded-lg px-3 py-1.5"
              :class="mode === 'problem' ? 'bg-slate-800 text-white dark:bg-slate-600' : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60'">
        Single problem
      </button>
    </div>

    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-4">{{ error }}</p>

    <div v-if="selecting" class="text-sm text-slate-400 dark:text-slate-500">Sending you back to the LMS…</div>

    <template v-else-if="mode === 'board' && boards">
      <div class="space-y-2 mb-6">
        <button v-for="b in boards" :key="b.slug" @click="selectBoard(b.slug)"
                class="w-full text-left border border-slate-200 dark:border-slate-800 rounded-lg px-4 py-2.5 hover:border-amber-400 dark:hover:border-amber-500/60">
          <div class="font-medium text-sm">{{ b.title }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500">{{ b.memberCount }} member(s) · {{ b.problemCount }} problem(s)</div>
        </button>
        <p v-if="!boards.length" class="text-sm text-slate-400 dark:text-slate-500">You don't own any boards yet — create one below.</p>
      </div>

      <div class="border-t border-slate-200 dark:border-slate-800 pt-4">
        <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">Or create a new board</p>
        <div class="flex gap-2">
          <input v-model="newTitle" @keyup.enter="createAndSelect" placeholder="Board title"
                 class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
          <button @click="createAndSelect" :disabled="creating || !newTitle.trim()"
                  class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
            {{ creating ? 'Creating…' : 'Create' }}
          </button>
        </div>
      </div>
    </template>

    <template v-else-if="mode === 'problem'">
      <p class="text-xs text-slate-400 dark:text-slate-500 mb-2">
        Students who open this activity go straight to solving this one problem — no board involved.
      </p>
      <div class="flex gap-2 mb-3">
        <input v-model="problemQ" @keyup.enter="loadProblems" placeholder="Search problems…"
               class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
        <select v-model="problemScope" @change="loadProblems"
                class="border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 text-sm">
          <option value="mine">Mine</option>
          <option value="public">Shared by others</option>
          <option value="all">All</option>
        </select>
      </div>
      <div v-if="problems" class="space-y-2">
        <button v-for="p in problems" :key="p.slug" @click="selectProblem(p.slug)"
                class="w-full text-left border border-slate-200 dark:border-slate-800 rounded-lg px-4 py-2.5 hover:border-amber-400 dark:hover:border-amber-500/60">
          <div class="font-medium text-sm">{{ p.title }}</div>
          <div class="text-[11px] text-slate-400 dark:text-slate-500">
            {{ p.level }}<span v-if="p.tags"> · {{ p.tags }}</span><span v-if="!p.mine"> · by {{ p.ownerName }}</span>
          </div>
        </button>
        <p v-if="!problems.length" class="text-sm text-slate-400 dark:text-slate-500">
          No problems found — try a different scope, or add one in Problem bank first.
        </p>
      </div>
    </template>
  </div>
</template>
