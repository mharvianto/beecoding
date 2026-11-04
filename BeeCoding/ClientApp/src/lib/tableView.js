import { ref, computed, watch } from 'vue';

const KEY = 'beecoding.tableView';

// The saved preference is synced across devices, so a 'table' chosen on a desktop would otherwise
// reach the phone and overflow it. On a narrow screen (< 640px) cards always win; the saved
// choice only applies from sm up.
const mq = window.matchMedia('(max-width: 639px)');
const narrow = ref(mq.matches);
mq.addEventListener('change', (e) => (narrow.value = e.matches));

// Storage can throw (private mode, blocked site data): fall back to the default and keep the choice in memory.
function initial() {
  try {
    const saved = localStorage.getItem(KEY);
    return saved === 'card' || saved === 'table' ? saved : 'table';
  } catch { return 'table'; }
}

const pref = ref(initial());
watch(pref, (v) => { try { localStorage.setItem(KEY, v); } catch { /* not saved, still applies this session */ } });

/** 'card' | 'table' — one shared preference for every admin data table in the app. */
export const tableView = computed({
  get: () => (narrow.value ? 'card' : pref.value),
  set: (v) => (pref.value = v),
});
