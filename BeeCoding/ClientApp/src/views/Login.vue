<script setup>
import { ref } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useAuth } from '../stores/auth';
import ThemeToggle from '../components/ThemeToggle.vue';

const auth = useAuth();
const router = useRouter();
const route = useRoute();
const email = ref('');
const password = ref('');
const error = ref('');
const busy = ref(false);

async function submit() {
  error.value = '';
  busy.value = true;
  try {
    await auth.login(email.value, password.value);
    router.push(route.query.r || '/boards');
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="min-h-[80vh] relative flex items-center justify-center px-4">
    <div class="absolute right-4 top-4"><ThemeToggle /></div>
    <div class="max-w-sm w-full">
      <h1 class="text-2xl font-bold text-amber-600 dark:text-amber-400 mb-1">🐝 BeeCoding</h1>
      <p class="text-slate-500 dark:text-slate-400 mb-6 text-sm">Sign in to your account</p>
      <form @submit.prevent="submit" class="space-y-3">
        <input v-model="email" type="email" placeholder="Email" required
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <input v-model="password" type="password" placeholder="Password" required
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
        <button :disabled="busy"
                class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
          {{ busy ? '…' : 'Sign in' }}
        </button>
      </form>
      <p class="text-sm text-slate-500 dark:text-slate-400 mt-4">
        No account? <RouterLink :to="{ path: '/register', query: route.query.r ? { r: route.query.r } : {} }" class="text-amber-600 dark:text-amber-400">Register</RouterLink>
      </p>
    </div>
  </div>
</template>
