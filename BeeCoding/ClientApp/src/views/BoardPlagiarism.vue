<script setup>
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import PlagiarismTable from '../components/PlagiarismTable.vue';
import SubmissionView from '../components/SubmissionView.vue';

const props = defineProps({ slug: { type: String, required: true } });
const router = useRouter();

const board = ref(null);
const pairs = ref([]);
const loading = ref(false);
const error = ref('');
const viewSubmission = ref(null);

onMounted(async () => {
  try {
    board.value = await api.get(`/api/boards/${props.slug}`);
    if (board.value.role === 'Student') { router.replace(`/boards/${props.slug}`); return; }
    loading.value = true;
    pairs.value = await api.get(`/api/boards/${props.slug}/plagiarism`);
  } catch (e) { error.value = e.message; }
  finally { loading.value = false; }
});
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-6" v-if="board">
    <RouterLink :to="`/boards/${slug}`" class="text-sm text-slate-400 dark:text-slate-500">&larr; back to {{ board.title }}</RouterLink>
    <h1 class="text-xl font-bold mt-2 mb-4">Plagiarism check</h1>
    <p v-if="error" class="text-red-600 dark:text-red-400 text-sm mb-3">{{ error }}</p>

    <PlagiarismTable :pairs="pairs" :loading="loading" @view="viewSubmission = $event" />

    <SubmissionView v-if="viewSubmission" :submission-id="viewSubmission.id" :author-name="viewSubmission.authorName"
                    @close="viewSubmission = null" />
  </div>
</template>
