<script>
import { api } from '../lib/api';

// One probe per page load: without the embedding model the button is not offered at all.
let enabledProbe = null;
const suggestionsOn = () => (enabledProbe ??= api.get('/api/tag-suggestions/enabled').then((r) => r?.enabled === true).catch(() => false));
</script>

<script setup>
import { ref, computed, onMounted } from 'vue';

/** Tag chips for the problem editor, from problems that mean something similar. Clicking one adds it; nothing is applied on its own. */
const props = defineProps({ title: String, statement: String, current: { type: String, default: '' } });
const emit = defineEmits(['add']);

const available = ref(false);
const busy = ref(false);
const message = ref('');
const tags = ref(null);   // null = not asked yet
onMounted(async () => { available.value = await suggestionsOn(); });

const have = computed(() => new Set((props.current || '').split(',').map((t) => t.trim().toLowerCase()).filter(Boolean)));
const shown = computed(() => (tags.value ?? []).filter((t) => !have.value.has(t.toLowerCase())));

async function ask() {
  busy.value = true; message.value = '';
  try {
    tags.value = await api.post('/api/tag-suggestions', { title: props.title, statement: props.statement });
    if (!tags.value.length) message.value = 'Write a title and a few sentences of the statement first.';
  } catch (e) { message.value = e.message; tags.value = null; }
  finally { busy.value = false; }
}
</script>

<template>
  <div v-if="available" class="flex items-center gap-2 flex-wrap text-xs">
    <button type="button" @click="ask" :disabled="busy" class="row-action-btn disabled:opacity-50">
      <span>🏷️</span><span>{{ busy ? 'Thinking…' : 'Suggest tags' }}</span>
    </button>
    <button v-for="t in shown" :key="t" type="button" @click="emit('add', t)" :title="`Add “${t}”`"
            class="rounded px-2 py-0.5 bg-violet-50 text-violet-700 hover:bg-violet-100 dark:bg-violet-500/10 dark:text-violet-300 dark:hover:bg-violet-500/20">+ {{ t }}</button>
    <span v-if="tags && !shown.length && tags.length" class="text-slate-400 dark:text-slate-500">Nothing new to suggest.</span>
    <span v-if="message" class="text-slate-500 dark:text-slate-400">{{ message }}</span>
    <span v-if="shown.length" class="text-[11px] text-slate-400 dark:text-slate-500">Guessed from similar problems; check them.</span>
  </div>
</template>
