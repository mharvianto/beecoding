<script setup>
import { onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';

const props = defineProps({ code: { type: String, required: true } });
const router = useRouter();
const error = ref('');

onMounted(async () => {
  try {
    const board = await api.post('/api/boards/join', { code: props.code });
    router.replace(`/boards/${board.slug}`);
  } catch (e) { error.value = e.message; }
});
</script>

<template>
  <div class="max-w-sm mx-auto mt-24 px-4 text-center">
    <template v-if="error">
      <p class="text-red-600 dark:text-red-400 mb-3">{{ error }}</p>
      <RouterLink to="/boards" class="text-amber-600 dark:text-amber-400 hover:underline">Go to your boards</RouterLink>
    </template>
    <p v-else class="text-slate-400 dark:text-slate-500">Joining board…</p>
  </div>
</template>
