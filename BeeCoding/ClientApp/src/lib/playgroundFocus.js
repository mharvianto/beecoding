import { ref, watch } from 'vue';

// "Focus mode" for the Playground: hides the app navbar so editor + stdin + output get
// the whole viewport. Per-browser convenience in plain localStorage (see lib/theme.js).
// App.vue only honours it on /playground, so it can never strand the user without a
// navbar elsewhere in the app.
const KEY = 'beecoding.playground.focus';

function read() {
  try { return localStorage.getItem(KEY) === '1'; } catch { return false; }
}

export const playgroundFocus = ref(read());
watch(playgroundFocus, (v) => {
  try { localStorage.setItem(KEY, v ? '1' : '0'); } catch { /* ignore */ }
});
