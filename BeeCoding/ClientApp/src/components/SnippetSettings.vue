<script setup>
import { ref, computed } from 'vue';
import { userSnippets, saveSnippets, MAX_SNIPPETS, MAX_BODY_CHARS, PREFIX_RE, SNIPPET_LANGS } from '../lib/snippets';
import { useConfirmDialog } from '../stores/confirmDialog';

const confirmDialog = useConfirmDialog();
const blank = () => ({ prefix: '', lang: '', description: '', body: '' });
const form = ref(blank());
const editing = ref(null);   // index being edited, or null for a new snippet
const error = ref('');
const langLabel = (id) => SNIPPET_LANGS.find((l) => l.id === id)?.label || 'C and C++';
const atLimit = computed(() => editing.value === null && userSnippets.value.length >= MAX_SNIPPETS);

function startEdit(i) { editing.value = i; form.value = { ...userSnippets.value[i] }; error.value = ''; }
function cancel() { editing.value = null; form.value = blank(); error.value = ''; }

function save() {
  const f = { ...form.value, prefix: form.value.prefix.trim(), description: form.value.description.trim() };
  if (!PREFIX_RE.test(f.prefix)) { error.value = 'The shortcut starts with a letter or _ and uses only letters, digits and _ (24 characters max).'; return; }
  if (!f.body.trim()) { error.value = 'Write what the shortcut should insert.'; return; }
  if (f.body.length > MAX_BODY_CHARS) { error.value = `A snippet can be ${MAX_BODY_CHARS} characters at most.`; return; }
  // The same shortcut may exist once per language: "C and C++" overlaps both.
  const clash = userSnippets.value.some((s, i) => i !== editing.value && s.prefix === f.prefix && (!s.lang || !f.lang || s.lang === f.lang));
  if (clash) { error.value = `You already have a snippet called “${f.prefix}” for this language.`; return; }
  const list = [...userSnippets.value];
  if (editing.value === null) {
    if (list.length >= MAX_SNIPPETS) { error.value = `You can keep ${MAX_SNIPPETS} snippets.`; return; }
    list.push(f);
  } else list[editing.value] = f;
  saveSnippets(list);
  cancel();
}

async function remove(i) {
  const s = userSnippets.value[i];
  if (!(await confirmDialog.ask(`Delete the snippet “${s.prefix}”?`, { confirmLabel: 'Delete' }))) return;
  saveSnippets(userSnippets.value.filter((_, j) => j !== i));
  if (editing.value === i) cancel();
}

const EXAMPLE = '#include <bits/stdc++.h>\nusing namespace std;\n\nint main() {\n    ios::sync_with_stdio(false);\n    cin.tie(nullptr);\n    $0\n    return 0;\n}';
function useExample() { form.value = { prefix: 'cpp_main', lang: 'cpp', description: 'Fast I/O template', body: EXAMPLE }; editing.value = null; }
</script>

<template>
  <section id="snippets" class="space-y-3 scroll-mt-4">
    <h2 class="font-semibold text-sm">Code snippets</h2>
    <p class="text-sm text-slate-500 dark:text-slate-400">
      Type a snippet's shortcut in the editor and pick it from the suggestions (Enter or Tab) to insert its text. They
      show up above the other suggestions, also when code intelligence is off, and follow you to every device.
    </p>

    <ul v-if="userSnippets.length" class="divide-y divide-slate-200 dark:divide-slate-800 border border-slate-200 dark:border-slate-800 rounded-xl">
      <li v-for="(s, i) in userSnippets" :key="s.prefix + s.lang" class="p-3 flex items-start gap-3">
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2 flex-wrap">
            <code class="text-sm font-semibold">{{ s.prefix }}</code>
            <span class="text-[11px] px-1.5 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{{ langLabel(s.lang) }}</span>
            <span v-if="s.description" class="text-xs text-slate-400 dark:text-slate-500 truncate">{{ s.description }}</span>
          </div>
          <pre class="mt-1 text-[11px] leading-snug text-slate-500 dark:text-slate-400 whitespace-pre-wrap break-all max-h-20 overflow-hidden">{{ s.body }}</pre>
        </div>
        <div class="flex gap-1 shrink-0">
          <button @click="startEdit(i)" class="row-action-btn row-action-btn--accent">Edit</button>
          <button @click="remove(i)" class="row-action-btn row-action-btn--danger">Delete</button>
        </div>
      </li>
    </ul>
    <p v-else class="text-sm text-slate-400 dark:text-slate-500">
      No snippets yet.
      <button @click="useExample" class="underline">Start from an example</button>
    </p>

    <form @submit.prevent="save" class="space-y-2 border border-slate-200 dark:border-slate-800 rounded-xl p-3">
      <h3 class="text-xs font-semibold text-slate-500 dark:text-slate-400">{{ editing === null ? 'New snippet' : 'Edit snippet' }}</h3>
      <div class="flex flex-col sm:flex-row gap-2">
        <input v-model="form.prefix" placeholder="Shortcut, e.g. fastio" maxlength="24" autocapitalize="off" spellcheck="false"
               class="sm:w-48 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm font-mono" />
        <select v-model="form.lang" aria-label="Language"
                class="sm:w-36 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-2 py-2 text-sm">
          <option v-for="l in SNIPPET_LANGS" :key="l.id" :value="l.id">{{ l.label }}</option>
        </select>
        <input v-model="form.description" placeholder="Description (optional)" maxlength="80"
               class="flex-1 border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm" />
      </div>
      <textarea v-model="form.body" rows="6" spellcheck="false" placeholder="What to insert. Use $0 for where the cursor ends up, or ${1:name} for a placeholder you tab through."
                class="w-full border border-slate-300 dark:border-slate-700 dark:bg-slate-800 rounded-lg px-3 py-2 text-sm font-mono"></textarea>
      <p class="text-[11px] text-slate-400 dark:text-slate-500">
        <code>$1</code>, <code>$2</code>… are tab stops, <code>${1:text}</code> is a stop with default text, <code>$0</code> is the final cursor
        position. Write <code>\$</code> for a literal dollar sign.
      </p>
      <p v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</p>
      <div class="flex items-center gap-2">
        <button type="submit" :disabled="atLimit"
                class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50">
          {{ editing === null ? 'Add snippet' : 'Save changes' }}
        </button>
        <button v-if="editing !== null" type="button" @click="cancel" class="row-action-btn">Cancel</button>
        <span v-if="atLimit" class="text-xs text-slate-400">Limit of {{ MAX_SNIPPETS }} snippets reached.</span>
      </div>
    </form>
  </section>
</template>
