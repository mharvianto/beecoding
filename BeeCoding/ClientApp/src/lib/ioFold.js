import { ref } from 'vue';

// Whether the bottom run panel (Run/Submit, stdin, output) under an editor is folded; the SplitPane bar toggles it. Per device.
const KEY = 'beecoding.io.folded';
export const ioFolded = ref((() => { try { return localStorage.getItem(KEY) === '1'; } catch { return false; } })());
export function setIoFolded(v) { ioFolded.value = v; try { localStorage.setItem(KEY, v ? '1' : '0'); } catch { /* the choice just does not persist */ } }
