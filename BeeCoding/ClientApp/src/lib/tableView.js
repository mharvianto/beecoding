import { ref, watch } from 'vue';

const KEY = 'beecoding.tableView';

// No saved preference yet: default to whichever the breakpoint would have picked, so a
// first-time visitor sees the same thing the old automatic sm:hidden/hidden sm:block
// split gave them.
function initial() {
  const saved = localStorage.getItem(KEY);
  return saved === 'card' || saved === 'table' ? saved : (window.innerWidth < 640 ? 'card' : 'table');
}

/** 'card' | 'table' — one shared preference for every admin data table in the app. */
export const tableView = ref(initial());
watch(tableView, (v) => localStorage.setItem(KEY, v));
