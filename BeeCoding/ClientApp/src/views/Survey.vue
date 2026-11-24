<script setup>
import { ref, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { docLang } from '../lib/docLang';
import { SECTIONS, forWho } from '../lib/surveyQuestions';

const auth = useAuth();
const who = computed(() => (auth.user?.role === 'Teacher' ? 'teacher' : 'student'));
const lang = computed(() => docLang.value);
const questions = computed(() => forWho(who.value));
const sections = computed(() => SECTIONS.filter((s) => !s.who || s.who === who.value)
  .map((s) => ({ ...s, items: questions.value.filter((q) => q.section === s.id) })).filter((s) => s.items.length));

const answers = ref({});
const loading = ref(true);
const busy = ref(false);
const error = ref('');
const saved = ref(false);
const had = ref(false);
const status = ref(null);   // { open, eligible, have, need, done, teacher } from /api/survey/status

onMounted(async () => {
  try {
    status.value = await api.get('/api/survey/status');
    answers.value = await api.get('/api/survey/mine'); had.value = Object.keys(answers.value).length > 0;
  } catch (e) { error.value = e.message; }
  finally { loading.value = false; }
});

const answered = computed(() => questions.value.filter((q) => answers.value[q.id]).length);

async function save() {
  busy.value = true; error.value = ''; saved.value = false;
  try {
    // only the questions this role sees, so a teacher's answers never carry a stale student one
    const body = {};
    for (const q of questions.value) if (answers.value[q.id]) body[q.id] = String(answers.value[q.id]);
    await api.put('/api/survey', body);
    saved.value = true; had.value = true;
  } catch (e) { error.value = e.message; }
  finally { busy.value = false; }
}
</script>

<template>
  <div class="max-w-2xl mx-auto px-4 py-8">
    <div class="flex items-start justify-between gap-3 mb-1">
      <h1 class="text-2xl font-bold">{{ lang === 'id' ? 'Survei BeeCoding' : 'BeeCoding survey' }}</h1>
    </div>
    <p class="text-sm text-slate-500 dark:text-slate-400 mb-6">
      {{ lang === 'id'
        ? 'Sekitar 5 menit. Semua pertanyaan boleh dilewati. Jawaban dilihat admin tanpa nama atau email, dan Anda bisa mengubahnya kapan saja.'
        : 'About 5 minutes. You can skip any question. Admins see the answers without your name or email, and you can change them any time.' }}
    </p>

    <p v-if="loading" class="text-sm text-slate-500 dark:text-slate-400">…</p>
    <div v-else-if="status && !status.open" class="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-5 text-sm">
      {{ lang === 'id' ? 'Survei ini sudah ditutup. Terima kasih!' : 'This survey is closed. Thank you!' }}
    </div>
    <div v-else-if="status && !status.eligible" class="rounded-xl border border-amber-200 dark:border-amber-500/30 bg-amber-50 dark:bg-amber-500/10 p-5 text-sm text-amber-900 dark:text-amber-200">
      <template v-if="status.teacher">{{ lang === 'id' ? 'Buat dulu satu soal atau board, lalu kembali ke sini supaya jawaban Anda berdasar pengalaman memakai BeeCoding.' : 'Create a problem or a board first, then come back, so your answers come from actually using BeeCoding.' }}</template>
      <template v-else>{{ lang === 'id'
        ? `Kerjakan dan kirim beberapa soal dulu (Practice atau board), lalu kembali ke sini. Progres: ${status.have}/${status.need} submission selesai.`
        : `Solve and submit a few problems first (Practice or a board), then come back. Progress: ${status.have}/${status.need} finished submissions.` }}</template>
      <div class="mt-3"><RouterLink to="/practice" class="font-semibold underline">Practice</RouterLink></div>
    </div>
    <form v-else @submit.prevent="save" class="space-y-8">
      <section v-for="s in sections" :key="s.id">
        <h2 class="font-semibold text-sm uppercase tracking-wide text-amber-700 dark:text-amber-400 mb-3">{{ s.title[lang] }}</h2>
        <div class="space-y-5">
          <fieldset v-for="q in s.items" :key="q.id" class="relative rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-4">
            <legend class="sr-only">{{ q.q[lang] }}</legend>
            <label :for="q.type === 'text' ? `q-${q.id}` : undefined" class="block text-sm font-medium mb-3">{{ q.q[lang] }}</label>

            <div v-if="q.type === 'scale' || q.type === 'nps'">
              <div class="flex flex-wrap gap-1.5" role="radiogroup" :aria-label="q.q[lang]">
                <button v-for="n in (q.type === 'nps' ? 11 : 5)" :key="n" type="button"
                        @click="answers[q.id] = String(q.type === 'nps' ? n - 1 : n)"
                        :aria-pressed="answers[q.id] === String(q.type === 'nps' ? n - 1 : n)"
                        class="w-10 h-10 rounded-lg border text-sm font-medium transition"
                        :class="answers[q.id] === String(q.type === 'nps' ? n - 1 : n)
                          ? 'bg-amber-500 border-amber-500 text-slate-900'
                          : 'border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:border-amber-400'">
                  {{ q.type === 'nps' ? n - 1 : n }}
                </button>
              </div>
              <div class="flex justify-between text-[11px] text-slate-500 dark:text-slate-400 mt-1.5">
                <span>{{ q.type === 'nps' ? (lang === 'id' ? 'Sama sekali tidak' : 'Not at all likely') : q.labels[lang][0] }}</span>
                <span>{{ q.type === 'nps' ? (lang === 'id' ? 'Sangat mungkin' : 'Extremely likely') : q.labels[lang][1] }}</span>
              </div>
            </div>

            <div v-else-if="q.type === 'choice'" class="space-y-1.5">
              <label v-for="[value, en, id] in q.options" :key="value" class="flex items-center gap-2 text-sm cursor-pointer">
                <input type="radio" :name="`q-${q.id}`" :value="value" v-model="answers[q.id]" class="accent-amber-500" />
                {{ lang === 'id' ? id : en }}
              </label>
            </div>

            <div v-else>
              <textarea :id="`q-${q.id}`" v-model="answers[q.id]" rows="3" maxlength="500"
                        class="w-full border border-slate-300 dark:border-slate-600 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm"></textarea>
              <div class="text-right text-[11px] tabular-nums text-slate-500 dark:text-slate-400 mt-1" aria-hidden="true">{{ (answers[q.id] || '').length }} / 500</div>
            </div>
          </fieldset>
        </div>
      </section>

      <p v-if="error" role="alert" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      <div class="flex items-center gap-3 sticky bottom-0 py-3 bg-[#f8fafc]/90 dark:bg-[#0b1120]/90 backdrop-blur">
        <button type="submit" :disabled="busy || !answered"
                class="bg-amber-500 hover:bg-amber-400 text-slate-900 font-medium rounded-lg px-5 py-2 disabled:opacity-50">
          {{ busy ? '…' : (had ? (lang === 'id' ? 'Perbarui jawaban' : 'Update answers') : (lang === 'id' ? 'Kirim' : 'Submit')) }}
        </button>
        <span class="text-xs text-slate-500 dark:text-slate-400">{{ answered }}/{{ questions.length }}</span>
        <span v-if="saved" role="status" class="text-sm text-emerald-700 dark:text-emerald-300">{{ lang === 'id' ? 'Terima kasih! Tersimpan.' : 'Thank you! Saved.' }}</span>
        <RouterLink v-if="auth.user?.isAdmin" to="/survey/results" class="ml-auto text-xs text-slate-500 dark:text-slate-400 hover:underline">
          {{ lang === 'id' ? 'Lihat hasil' : 'View results' }}
        </RouterLink>
      </div>
    </form>
  </div>
</template>
