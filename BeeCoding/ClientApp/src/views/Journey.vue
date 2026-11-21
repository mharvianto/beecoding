<script setup>
import { tr } from '../lib/i18n';
import Icon from '../components/Icon.vue';
import LevelBadge from '../components/LevelBadge.vue';
import { ref, onMounted, watch } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../lib/api';
import { useAuth } from '../stores/auth';
import { useProgress } from '../stores/progress';

/** A guided path through Practice: worlds of ordered problems (easy to hard, any can be skipped), each ending in a boss. */
const props = defineProps({ id: { type: String, default: '' } });
const router = useRouter();
const auth = useAuth();
const progress = useProgress();

const list = ref([]);
const map = ref(null);
const defeated = ref([]);
const error = ref('');
const loading = ref(true);

async function load() {
  loading.value = true; error.value = '';
  try {
    if (props.id) {
      map.value = await api.get(`/api/journeys/${encodeURIComponent(props.id)}`);
      if (map.value.newlyDefeated.length) { defeated.value = map.value.newlyDefeated; progress.refresh(); }
    } else {
      list.value = await api.get('/api/journeys');
      if (list.value.length === 1) router.replace(`/journey/${list.value[0].id}`);
    }
  } catch (e) { error.value = e.message; }
  finally { loading.value = false; }
}

async function skip(slug, undo) {
  try {
    await (undo ? api.del : api.post)(`/api/journeys/${props.id}/skip/${slug}`);
    await load();
  } catch (e) { error.value = e.message; }
}

const stateBox = {
  done: 'border-emerald-300 dark:border-emerald-500/40 bg-emerald-50/60 dark:bg-emerald-500/5',
  next: 'border-amber-400 bg-amber-50 dark:bg-amber-500/10',
  skipped: 'border-dashed border-amber-400 dark:border-amber-500/50',
  todo: 'border-slate-200 dark:border-slate-700',
};

onMounted(load);
watch(() => props.id, () => { map.value = null; defeated.value = []; load(); });
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="flex items-baseline justify-between gap-3 flex-wrap mb-4">
      <h1 class="text-2xl font-semibold">{{ map ? map.title : tr('Journey') }}</h1>
      <RouterLink v-if="auth.user?.isAdmin" to="/journey/edit" class="row-action-btn"><Icon name="pencil" /> {{ tr('Edit journeys') }}</RouterLink>
    </div>
    <p class="text-sm text-slate-600 dark:text-slate-300 max-w-3xl mb-4">{{ tr('A guided path through Practice. Problems come in order, from easy to hard, grouped into worlds. You can skip any problem and come back to it later. Finish enough problems in a world to unlock its boss: beat it for bonus XP and a badge. Your progress comes from problems you have already solved.') }}</p>
    <p v-if="error" class="text-sm text-rose-600 mb-3">{{ error }}</p>
    <div class="max-w-3xl">
    <p v-if="loading" class="text-sm text-slate-500">{{ tr('Loading…') }}</p>

    <!-- list (only when there is more than one journey) -->
    <template v-else-if="!id">
      <p v-if="!list.length" class="text-sm text-slate-500">{{ tr('No journey is open yet.') }} <RouterLink v-if="auth.user?.isAdmin" to="/journey/edit" class="underline">{{ tr('Create one') }}</RouterLink></p>
      <RouterLink v-for="j in list" :key="j.id" :to="`/journey/${j.id}`"
                  class="block rounded-xl border border-slate-200 dark:border-slate-700 px-4 py-3 mb-2 hover:border-amber-400">
        <div class="font-medium">{{ j.title }}</div>
        <div class="text-xs text-slate-500 dark:text-slate-400">{{ tr('{done} of {total} problems solved', { done: j.done, total: j.total }) }}</div>
      </RouterLink>
    </template>

    <template v-else-if="map">
      <div v-for="d in defeated" :key="d.world" class="mb-3 rounded-xl border border-amber-400 bg-amber-50 dark:bg-amber-500/10 px-4 py-3 text-sm">
        <Icon name="crown" /> <b>{{ tr('Boss defeated') }}</b> — {{ d.world }}<span v-if="d.badge"> · {{ tr('Badge: {name}', { name: d.badge }) }}</span> · +{{ d.xp }} XP
      </div>

      <section v-for="(w, wi) in map.worlds" :key="w.id" class="mb-6">
        <div class="flex items-baseline justify-between mb-2">
          <h2 class="font-semibold">{{ tr('World {n}', { n: wi + 1 }) }} · {{ w.name }}</h2>
          <span class="text-xs text-slate-500 dark:text-slate-400">{{ tr('{done} of {total} done', { done: w.done, total: w.count }) }}</span>
        </div>
        <ul class="space-y-1.5">
          <li v-for="(it, i) in w.items" :key="it.slug" class="flex items-center gap-3 rounded-xl border px-3 py-2" :class="stateBox[it.state]">
            <span class="w-6 h-6 shrink-0 rounded-full flex items-center justify-center text-xs font-bold bg-slate-100 dark:bg-slate-800">
              <Icon v-if="it.state === 'done'" name="check" class="text-emerald-600" />
              <Icon v-else-if="it.state === 'skipped'" name="skip" class="text-amber-600" />
              <template v-else>{{ i + 1 }}</template>
            </span>
            <div class="min-w-0 flex-1">
              <RouterLink :to="`/practice/${it.slug}`" class="font-medium hover:underline">{{ it.title }}</RouterLink>
              <LevelBadge :level="it.level" class="ml-2" />
              <div v-if="it.state === 'skipped'" class="text-[11px] text-amber-700 dark:text-amber-300">{{ tr('Skipped · come back') }}</div>
              <div v-else-if="it.state === 'next'" class="text-[11px] text-amber-700 dark:text-amber-300">{{ tr('Next up') }}</div>
            </div>
            <RouterLink v-if="it.state === 'next'" :to="`/practice/${it.slug}`" class="rounded-lg font-medium bg-amber-500 text-slate-900 text-xs px-3 py-1.5">{{ tr('Continue') }}</RouterLink>
            <button v-if="it.state === 'next'" @click="skip(it.slug, false)" class="row-action-btn"><Icon name="skip" /> {{ tr('Skip for now') }}</button>
            <button v-else-if="it.state === 'skipped'" @click="skip(it.slug, true)" class="row-action-btn">{{ tr('Put back') }}</button>
          </li>
        </ul>

        <div v-if="w.boss" class="mt-2 flex items-center gap-3 rounded-xl px-4 py-3 bg-slate-800 text-slate-100"
             :class="w.boss.state === 'ready' ? 'ring-2 ring-amber-400' : ''">
          <Icon name="crown" class="text-amber-400 w-6 h-6 shrink-0" />
          <div class="min-w-0 flex-1">
            <div class="text-[11px] uppercase tracking-wide text-slate-300">{{ tr('Boss of World {n}', { n: wi + 1 }) }}</div>
            <div class="font-semibold">{{ w.boss.title }} <LevelBadge :level="w.boss.level" class="ml-1" /></div>
            <div class="text-xs text-slate-300">
              <template v-if="w.boss.state === 'locked'"><Icon name="lock" /> {{ tr('Finish {need} problems to unlock (skipped ones do not count) · {left} more', { need: w.boss.need, left: w.boss.need - w.done }) }}</template>
              <template v-else-if="w.boss.state === 'defeated'">{{ tr('Defeated') }}<span v-if="w.boss.badge"> · {{ w.boss.badge }}</span></template>
              <template v-else>{{ tr('Ready · +{xp} XP', { xp: w.boss.bossXp }) }}<span v-if="w.boss.badge"> · {{ w.boss.badge }}</span></template>
            </div>
          </div>
          <RouterLink v-if="w.boss.state === 'ready'" :to="`/practice/${w.boss.slug}`" class="rounded-lg font-medium bg-amber-500 text-slate-900 text-xs px-3 py-1.5">{{ tr('Face the boss') }}</RouterLink>
        </div>
      </section>
    </template>
    </div>
  </div>
</template>
