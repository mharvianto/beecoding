<script setup>
import { tr } from '../lib/i18n';
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { docLang } from '../lib/docLang';
import Icon from './Icon.vue';

// Soft invitation to the feedback survey, only for someone who may answer already (used the judge, see SurveyController)
// and has not. "Not now" hides it for a week, "Don't ask again" for good; per device, in localStorage, like the notification offer.
const auth = useAuth();
const router = useRouter();
const key = () => `beecoding.surveyOffer.${auth.user?.id}`;
const shown = ref(false);

function hiddenNow() { try { return Number(localStorage.getItem(key()) || 0) > Date.now(); } catch { return false; } }
onMounted(async () => {
  if (hiddenNow()) return;
  try {
    const s = await api.get('/api/survey/status');
    shown.value = s.open && s.eligible && !s.done;
  } catch { /* no banner this time */ }
});

function hide(forever) {
  try { localStorage.setItem(key(), String(forever ? Number.MAX_SAFE_INTEGER : Date.now() + 7 * 86400_000)); } catch { /* shows again next load */ }
  shown.value = false;
}
const open = () => { shown.value = false; router.push('/survey'); };
</script>

<template>
  <div v-if="shown" role="region" :aria-label="tr('Notice')" class="shrink-0 bg-amber-50 dark:bg-amber-500/10 border-b border-amber-200 dark:border-amber-500/20 text-sm text-amber-900 dark:text-amber-200">
    <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center gap-x-3 gap-y-1 flex-wrap">
      <span><Icon name="message" /> {{ docLang === 'id' ? 'Bantu kami memperbaiki BeeCoding — survei singkat, sekitar 5 menit.' : 'Help us improve BeeCoding — a short survey, about 5 minutes.' }}</span>
      <button @click="open" class="font-semibold underline hover:no-underline">{{ docLang === 'id' ? 'Isi survei' : 'Take the survey' }}</button>
      <span class="ml-auto flex items-center gap-3 text-xs text-amber-700 dark:text-amber-300">
        <button @click="hide(false)" class="hover:underline">{{ docLang === 'id' ? 'Nanti saja' : 'Not now' }}</button>
        <button @click="hide(true)" class="hover:underline">{{ docLang === 'id' ? 'Jangan tanya lagi' : "Don't ask again" }}</button>
      </span>
    </div>
  </div>
</template>
