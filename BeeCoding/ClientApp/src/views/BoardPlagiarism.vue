<script setup>
import { ref, onMounted } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { api } from '../lib/api';
import PlagiarismTable from '../components/PlagiarismTable.vue';
import SubmissionView from '../components/SubmissionView.vue';
import SubmissionDiffView from '../components/SubmissionDiffView.vue';

const props = defineProps({ slug: { type: String, required: true } });
const router = useRouter();
const route = useRoute();

const board = ref(null);
const problems = ref([]);
const problem = ref(typeof route.query.problem === 'string' ? route.query.problem : '');   // '' = every problem
const pairs = ref({ groups: [], pairs: [] });
const loading = ref(false);
const error = ref('');
const viewSubmission = ref(null);
const compareData = ref(null);

onMounted(async () => {
  try {
    board.value = await api.get(`/api/boards/${props.slug}`);
    if (board.value.role === 'Student') { router.replace(`/boards/${props.slug}`); return; }
    problems.value = await api.get(`/api/boards/${props.slug}/problems`);
    await check();
  } catch (e) { error.value = e.message; }
});

async function check() {
  error.value = ''; loading.value = true;
  try {
    pairs.value = await api.get(`/api/boards/${props.slug}/plagiarism${problem.value ? `?problem=${encodeURIComponent(problem.value)}` : ''}`);
  } catch (e) { error.value = e.message; pairs.value = { groups: [], pairs: [] }; }
  finally { loading.value = false; }
}
function pick() {
  router.replace({ query: problem.value ? { problem: problem.value } : {} });   // keep the choice in the URL so it survives a refresh and can be linked
  check();
}
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-6" v-if="board">
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-500 dark:text-slate-400">&larr; back to {{ board.title }}</RouterLink>
    <h1 class="text-xl font-bold mt-2 mb-4">Plagiarism check</h1>
    <label class="flex items-center gap-2 text-sm mb-3">
      <span class="text-slate-500 dark:text-slate-400">Problem</span>
      <select v-model="problem" @change="pick"
              class="border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-2 py-1 max-w-xs">
        <option value="">All problems</option>
        <option v-for="p in problems" :key="p.slug" :value="p.slug">{{ p.title }}</option>
      </select>
    </label>
    <p v-if="error" role="alert" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ error }}</p>

    <PlagiarismTable :result="pairs" :loading="loading" @view="viewSubmission = $event" @compare="compareData = $event" />

    <SubmissionView v-if="viewSubmission" :submission-id="viewSubmission.id" :author-name="viewSubmission.authorName"
                    @close="viewSubmission = null" />
    <SubmissionDiffView v-if="compareData" :submission-a-id="compareData.submissionAId" :submission-b-id="compareData.submissionBId"
                         :label-a="compareData.userAName" :label-b="compareData.userBName" @close="compareData = null" />
  </div>
</template>
