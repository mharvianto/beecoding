<script setup>
import { ref } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useAuth } from '../stores/auth';
import ThemeToggle from '../components/ThemeToggle.vue';

const auth = useAuth();
const router = useRouter();
const route = useRoute();
const form = ref({ displayName: '', email: '', password: '', role: 'Student', teacherCode: '' });
const error = ref('');
const busy = ref(false);

async function submit() {
  error.value = '';
  busy.value = true;
  try {
    await auth.register(form.value);
    router.push(route.query.r || '/boards');
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="max-w-sm mx-auto mt-20 px-4 relative">
    <div class="absolute right-4 -top-10"><ThemeToggle /></div>
    <h1 class="text-2xl font-bold text-amber-600 dark:text-amber-400 mb-6">Create your account</h1>
    <form @submit.prevent="submit" class="space-y-3">
      <input v-model="form.displayName" placeholder="Display name" required
             class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
      <input v-model="form.email" type="email" placeholder="Email" required
             class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
      <input v-model="form.password" type="password" placeholder="Password (min 6 chars)" required
             class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
      <div class="flex gap-2">
        <label class="flex-1 border rounded-lg px-3 py-2 cursor-pointer text-sm"
               :class="form.role === 'Student'
                 ? 'border-amber-500 bg-amber-50 dark:bg-amber-500/10'
                 : 'border-slate-300 dark:border-slate-700'">
          <input type="radio" value="Student" v-model="form.role" class="mr-2" />Student
        </label>
        <label class="flex-1 border rounded-lg px-3 py-2 cursor-pointer text-sm"
               :class="form.role === 'Teacher'
                 ? 'border-amber-500 bg-amber-50 dark:bg-amber-500/10'
                 : 'border-slate-300 dark:border-slate-700'">
          <input type="radio" value="Teacher" v-model="form.role" class="mr-2" />Teacher
        </label>
      </div>
      <input v-if="form.role === 'Teacher'" v-model="form.teacherCode" placeholder="Teacher invite code"
             class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
      <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      <button :disabled="busy"
              class="w-full bg-amber-500 hover:bg-amber-600 text-white rounded-lg py-2 font-medium disabled:opacity-50">
        {{ busy ? '…' : 'Register' }}
      </button>
    </form>
    <p class="text-xs text-slate-400 dark:text-slate-500 mt-3">
      By creating an account you agree to the
      <RouterLink to="/terms" class="text-amber-600 dark:text-amber-400">Terms &amp; Agreement</RouterLink>
      and <RouterLink to="/privacy" class="text-amber-600 dark:text-amber-400">Privacy Policy</RouterLink>.
    </p>
    <p class="text-sm text-slate-500 dark:text-slate-400 mt-4">
      Have an account? <RouterLink :to="{ path: '/login', query: route.query.r ? { r: route.query.r } : {} }" class="text-amber-600 dark:text-amber-400">Sign in</RouterLink>
    </p>
  </div>
</template>
