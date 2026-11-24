<script setup>
import { tr } from '../lib/i18n';
import Icon from '../components/Icon.vue';
import LevelBadge from '../components/LevelBadge.vue';
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useConfirmDialog } from '../stores/confirmDialog';

/** Platform admins build journeys here: worlds, the problems of each in order, and a boss per world. The whole structure is saved at once. */
const router = useRouter();
const auth = useAuth();
const confirmDialog = useConfirmDialog();

const journeys = ref([]);
const cur = ref(null);          // { id|null, title, published, worlds: [{ name, unlockAfter, bossXp, badgeName, boss, items }] }
const error = ref('');
const saved = ref(false);
const q = ref('');
const found = ref([]);
const target = ref(0);          // index of the world the search adds to

const blankWorld = () => ({ name: '', unlockAfter: 0, bossXp: 100, badgeName: '', boss: null, items: [] });

async function load() {
  try { journeys.value = await api.get('/api/journeys/admin'); } catch (e) { error.value = e.message; }
}
function edit(j) {
  saved.value = false; error.value = '';
  cur.value = j ? JSON.parse(JSON.stringify(j)) : { id: null, title: '', published: false, worlds: [blankWorld()] };
  target.value = 0;
}
async function search() {
  if (!q.value.trim()) { found.value = []; return; }
  try { found.value = (await api.get(`/api/practice?q=${encodeURIComponent(q.value.trim())}&pageSize=8`)).items; } catch (e) { error.value = e.message; }
}
const add = (p) => { cur.value.worlds[target.value]?.items.push({ slug: p.slug, title: p.title, level: p.level }); };
const setBoss = (p) => { const w = cur.value.worlds[target.value]; if (w) w.boss = { slug: p.slug, title: p.title, level: p.level }; };
function move(w, i, d) {
  const j = i + d;
  if (j < 0 || j >= w.items.length) return;
  [w.items[i], w.items[j]] = [w.items[j], w.items[i]];
}

async function save() {
  error.value = ''; saved.value = false;
  const body = {
    title: cur.value.title, published: cur.value.published,
    worlds: cur.value.worlds.map((w) => ({
      name: w.name, unlockAfter: Number(w.unlockAfter) || 0, bossSlug: w.boss?.slug ?? null,
      bossXp: Number(w.bossXp) || 0, badgeName: w.badgeName, slugs: w.items.map((i) => i.slug),
    })),
  };
  try {
    if (cur.value.id) await api.put(`/api/journeys/admin/${cur.value.id}`, body);
    else cur.value.id = (await api.post('/api/journeys/admin', body)).id;
    saved.value = true;
    await load();
  } catch (e) { error.value = e.message; }
}
async function remove() {
  if (!(await confirmDialog.ask(tr('Delete the journey “{name}”? Students keep their solves; only the path goes.', { name: cur.value.title }), { confirmLabel: tr('Delete') }))) return;
  try { await api.del(`/api/journeys/admin/${cur.value.id}`); cur.value = null; await load(); } catch (e) { error.value = e.message; }
}

onMounted(() => { if (!auth.user?.isAdmin) router.replace('/journey'); else load(); });
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-6 sm:py-8">
    <div class="flex items-baseline justify-between gap-3 flex-wrap mb-4">
      <h1 class="text-2xl font-semibold">{{ tr('Edit journeys') }}</h1>
      <RouterLink to="/journey" class="text-sm hover:underline">{{ tr('Back to the journey') }}</RouterLink>
    </div>
    <p v-if="error" class="text-sm text-rose-600 mb-3">{{ error }}</p>

    <div class="flex flex-wrap gap-2 mb-4">
      <button v-for="j in journeys" :key="j.id" @click="edit(j)" class="row-action-btn" :class="cur?.id === j.id ? 'row-action-btn--accent' : ''">
        {{ j.title }}<span v-if="!j.published" class="text-[11px] opacity-70"> · {{ tr('Draft') }}</span>
      </button>
      <button @click="edit(null)" class="row-action-btn"><Icon name="plus" /> {{ tr('New journey') }}</button>
    </div>

    <div v-if="cur" class="grid gap-4 lg:grid-cols-[1fr_320px] items-start">
      <div class="space-y-4">
        <div class="flex flex-wrap items-center gap-3">
          <input :aria-label="tr('Journey title')" v-model="cur.title" :placeholder="tr('Journey title')" maxlength="120"
                 class="flex-1 min-w-[200px] px-3 py-2 rounded-lg border border-slate-300 dark:border-slate-600 bg-transparent">
          <label class="text-sm flex items-center gap-1.5"><input type="checkbox" v-model="cur.published"> {{ tr('Published') }}</label>
        </div>

        <section v-for="(w, wi) in cur.worlds" :key="wi" class="rounded-xl border p-3 space-y-2"
                 :class="target === wi ? 'border-amber-400' : 'border-slate-200 dark:border-slate-700'" @click="target = wi">
          <div class="flex items-center gap-2 flex-wrap">
            <span class="text-sm font-semibold">{{ tr('World {n}', { n: wi + 1 }) }}</span>
            <input :aria-label="tr('World name')" v-model="w.name" :placeholder="tr('World name')" maxlength="120"
                   class="flex-1 min-w-[140px] px-2 py-1 rounded border border-slate-300 dark:border-slate-600 bg-transparent text-sm">
            <button @click.stop="cur.worlds.splice(wi, 1); target = 0" class="row-action-btn row-action-btn--danger"><Icon name="trash" /> {{ tr('Remove') }}</button>
          </div>
          <ul class="space-y-1">
            <li v-for="(it, i) in w.items" :key="it.slug" class="flex items-center gap-2 text-sm">
              <span class="w-5 text-slate-500 dark:text-slate-400">{{ i + 1 }}</span>
              <span class="flex-1 min-w-0 truncate">{{ it.title }}</span>
              <LevelBadge :level="it.level" />
              <button @click.stop="move(w, i, -1)" class="row-action-btn" :aria-label="tr('Move up')"><Icon name="arrowup" /></button>
              <button @click.stop="move(w, i, 1)" class="row-action-btn" :aria-label="tr('Move down')"><Icon name="arrowup" class="rotate-180" /></button>
              <button @click.stop="w.items.splice(i, 1)" class="row-action-btn row-action-btn--danger" :aria-label="tr('Remove')"><Icon name="x" /></button>
            </li>
            <li v-if="!w.items.length" class="text-xs text-slate-500 dark:text-slate-400">{{ tr('No problems yet — search on the right and add some.') }}</li>
          </ul>
          <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm pt-1 border-t border-slate-200 dark:border-slate-700">
            <label class="flex items-center gap-1.5">{{ tr('Boss opens after') }}
              <input type="number" min="0" :max="w.items.length" v-model="w.unlockAfter" class="w-16 px-2 py-1 rounded border border-slate-300 dark:border-slate-600 bg-transparent">
              {{ tr('of {n} solved', { n: w.items.length }) }}</label>
            <span class="flex items-center gap-1.5"><Icon name="crown" class="text-amber-500" />
              <template v-if="w.boss">{{ w.boss.title }} <button @click.stop="w.boss = null" class="row-action-btn" :aria-label="tr('Remove')"><Icon name="x" /></button></template>
              <template v-else><span class="text-slate-500 dark:text-slate-400">{{ tr('no boss — pick one on the right') }}</span></template>
            </span>
            <label class="flex items-center gap-1.5">+<input type="number" min="0" max="1000" v-model="w.bossXp" class="w-20 px-2 py-1 rounded border border-slate-300 dark:border-slate-600 bg-transparent"> XP</label>
            <input :aria-label="tr('Badge name')" v-model="w.badgeName" :placeholder="tr('Badge name')" maxlength="60" class="w-40 px-2 py-1 rounded border border-slate-300 dark:border-slate-600 bg-transparent">
          </div>
        </section>

        <div class="flex flex-wrap items-center gap-3">
          <button @click="cur.worlds.push(blankWorld()); target = cur.worlds.length - 1" class="row-action-btn"><Icon name="plus" /> {{ tr('Add world') }}</button>
          <button @click="save" class="rounded-lg font-medium bg-amber-500 text-slate-900 px-4 py-1.5 text-sm">{{ tr('Save') }}</button>
          <button v-if="cur.id" @click="remove" class="row-action-btn row-action-btn--danger"><Icon name="trash" /> {{ tr('Delete') }}</button>
          <span v-if="saved" class="text-sm text-emerald-600">{{ tr('Saved') }}</span>
        </div>
      </div>

      <aside class="rounded-xl border border-slate-200 dark:border-slate-700 p-3 space-y-2">
        <div class="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{{ tr('Add from the bank') }}</div>
        <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Goes into the world you last clicked: World {n}', { n: target + 1 }) }}</div>
        <input :aria-label="tr('Search title or tag…')" v-model="q" @input="search" :placeholder="tr('Search title or tag…')" class="w-full px-2 py-1.5 rounded border border-slate-300 dark:border-slate-600 bg-transparent text-sm">
        <ul class="space-y-1">
          <li v-for="p in found" :key="p.slug" class="flex items-center gap-2 text-sm">
            <span class="flex-1 min-w-0 truncate">{{ p.title }}</span>
            <LevelBadge :level="p.level" />
            <button @click="add(p)" class="row-action-btn">{{ tr('Add') }}</button>
            <button @click="setBoss(p)" class="row-action-btn"><Icon name="crown" /></button>
          </li>
        </ul>
        <p class="text-xs text-slate-500 dark:text-slate-400">{{ tr('Only public problems can be used. A problem can appear once in a journey.') }}</p>
      </aside>
    </div>
  </div>
</template>
