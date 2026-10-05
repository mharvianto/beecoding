<script setup>
import { ref, onMounted } from 'vue';
import { api } from '../lib/api';
import { useRouter, useRoute } from 'vue-router';
import { useAuth } from '../stores/auth';
import ThemeToggle from '../components/ThemeToggle.vue';
import GoogleButton from '../components/GoogleButton.vue';

const auth = useAuth();
const router = useRouter();
const route = useRoute();
const form = ref({ displayName: '', email: '', password: '', role: 'Student', teacherCode: '' });
const error = ref('');
const busy = ref(false);

const googleOn = ref(false);
const google = ref(null);   // { ticket, email } when finishing a sign-up that started with Google

onMounted(async () => {
  try { googleOn.value = (await api.get('/api/auth/config')).google === true; } catch { /* hidden */ }
  if (route.query.google) {
    const ticket = String(route.query.google);
    try {
      const info = await api.post('/api/auth/google/pending', { ticket });
      google.value = { ticket, email: info.email };
      form.value.displayName = info.name;
    } catch (e) { error.value = e.message; }
  }
});

async function submit() {
  error.value = '';
  busy.value = true;
  try {
    if (google.value) {
      await auth.googleRegister({ ticket: google.value.ticket, displayName: form.value.displayName, role: form.value.role, teacherCode: form.value.teacherCode });
    } else {
      await auth.register(form.value);
    }
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
    <template v-if="googleOn && !google">
      <GoogleButton label="Sign up with Google" :return-to="route.query.r ? String(route.query.r) : ''" />
      <div class="flex items-center gap-3 text-xs text-slate-400 dark:text-slate-500 my-4">
        <span class="flex-1 h-px bg-slate-200 dark:bg-slate-700"></span>or with email<span class="flex-1 h-px bg-slate-200 dark:bg-slate-700"></span>
      </div>
    </template>
    <p v-if="google" class="text-sm text-slate-600 dark:text-slate-300 mb-3">
      Almost done. Signing up with Google as <b>{{ google.email }}</b>; choose how you will use BeeCoding.
    </p>
    <form @submit.prevent="submit" class="space-y-3">
      <input v-model="form.displayName" placeholder="Display name" required
             class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
      <template v-if="!google">
        <input v-model="form.email" type="email" placeholder="Email" required
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
        <input v-model="form.password" type="password" placeholder="Password (min 8 chars)" required
               class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2" />
      </template>
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
