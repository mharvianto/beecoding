<script setup>
import { ref, computed, onMounted, watch } from 'vue';
import { api } from '../lib/api';

// Like + "report a problem" for the problem being solved. `kind` picks the API: a practice (bank) problem is
// addressed by `slug`; a board problem by `boardSlug` + `slug` (its own slug).
const props = defineProps({
  kind: { type: String, required: true },          // 'practice' | 'board'
  slug: { type: String, required: true },
  boardSlug: { type: String, default: '' },
});

const base = computed(() => (props.kind === 'practice'
  ? `/api/practice/${props.slug}`
  : `/api/boards/${props.boardSlug}/problems/${props.slug}`));

const fb = ref({ likes: 0, liked: false, reported: false });
const busy = ref(false);
const modal = ref(false);
const category = ref('');
const message = ref('');
const error = ref('');
const sent = ref(false);

const CATEGORIES = [
  ['WrongInput', 'Input doesn’t match the statement', 'The test input has a different format than described.'],
  ['ConstraintViolation', 'Input breaks the constraints', 'A test input is outside the limits the statement promises.'],
  ['WrongOutput', 'Expected output looks wrong', 'A correct solution fails, or the expected answer is incorrect.'],
  ['StatementUnclear', 'Statement is unclear or has a typo', 'Missing detail, ambiguity, or a mistake in the text.'],
  ['Other', 'Something else', ''],
];

async function load() {
  try { fb.value = await api.get(`${base.value}/feedback`); } catch { /* the buttons just stay inert */ }
}
onMounted(load);
watch(base, load);

async function toggleLike() {
  if (busy.value) return;
  busy.value = true;
  const was = fb.value;
  fb.value = { ...was, liked: !was.liked, likes: Math.max(0, was.likes + (was.liked ? -1 : 1)) };   // optimistic
  try { fb.value = await (was.liked ? api.del(`${base.value}/like`) : api.put(`${base.value}/like`)); }
  catch { fb.value = was; }
  finally { busy.value = false; }
}

function openReport() { modal.value = true; sent.value = false; error.value = ''; category.value = ''; message.value = ''; }
async function submit() {
  error.value = '';
  busy.value = true;
  try {
    await api.post(`${base.value}/report`, { category: category.value, message: message.value });
    sent.value = true;
    fb.value = { ...fb.value, reported: true };
  } catch (e) { error.value = e.message; } finally { busy.value = false; }
}
const ready = computed(() => category.value && message.value.trim().length >= 10);
</script>

<template>
  <span class="inline-flex items-center gap-1.5">
    <button type="button" @click="toggleLike" :aria-pressed="fb.liked" :title="fb.liked ? 'Remove your like' : 'Like this problem'"
            class="inline-flex items-center gap-1 text-xs rounded-md border px-2 py-0.5 transition-colors"
            :class="fb.liked
              ? 'border-rose-300 dark:border-rose-500/40 bg-rose-50 dark:bg-rose-500/10 text-rose-600 dark:text-rose-300'
              : 'border-slate-300 dark:border-slate-700 text-slate-500 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-300'">
      <span aria-hidden="true">{{ fb.liked ? '♥' : '♡' }}</span><span class="tabular-nums">{{ fb.likes }}</span>
    </button>
    <button type="button" @click="openReport" title="Report a problem with this problem"
            class="inline-flex items-center gap-1 text-xs rounded-md border border-slate-300 dark:border-slate-700 px-2 py-0.5 text-slate-500 dark:text-slate-400 hover:text-amber-700 dark:hover:text-amber-300">
      <span aria-hidden="true">🚩</span><span>{{ fb.reported ? 'Reported' : 'Report' }}</span>
    </button>
  </span>

  <Teleport to="body">
    <div v-if="modal" class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/50" @click.self="modal = false" @keydown.esc="modal = false">
      <div class="w-full max-w-md bg-white dark:bg-slate-900 rounded-2xl shadow-xl border border-slate-200 dark:border-slate-800 p-5 space-y-3 max-h-[90vh] overflow-y-auto" role="dialog" aria-label="Report a problem">
        <template v-if="!sent">
          <h2 class="font-semibold">Report a problem</h2>
          <p class="text-xs text-slate-500 dark:text-slate-400">Found something wrong with this problem? Tell the author what, and they'll take a look.</p>
          <div class="space-y-1.5">
            <label v-for="[key, label, hint] in CATEGORIES" :key="key"
                   class="flex items-start gap-2 rounded-lg border px-3 py-2 cursor-pointer text-sm"
                   :class="category === key ? 'border-amber-500 bg-amber-50 dark:bg-amber-500/10' : 'border-slate-200 dark:border-slate-700'">
              <input type="radio" :value="key" v-model="category" class="mt-1" />
              <span><span class="block">{{ label }}</span><span v-if="hint" class="block text-[11px] text-slate-400 dark:text-slate-500">{{ hint }}</span></span>
            </label>
          </div>
          <div>
            <textarea v-model="message" rows="4" maxlength="1000" placeholder="Describe it: which input or line is wrong, and what you expected (at least 10 characters)."
                      class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm"></textarea>
            <div class="text-[11px] text-slate-400 dark:text-slate-500 text-right">{{ message.length }}/1000</div>
          </div>
          <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
          <div class="flex gap-2 justify-end">
            <button @click="modal = false" class="text-sm text-slate-500 dark:text-slate-400 px-3 py-2">Cancel</button>
            <button @click="submit" :disabled="busy || !ready" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
              {{ busy ? '…' : 'Send report' }}
            </button>
          </div>
        </template>
        <template v-else>
          <h2 class="font-semibold">Thanks for the report</h2>
          <p class="text-sm text-slate-600 dark:text-slate-300">The author has been asked to review it.</p>
          <div class="flex justify-end"><button @click="modal = false" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium">Close</button></div>
        </template>
      </div>
    </div>
  </Teleport>
</template>
