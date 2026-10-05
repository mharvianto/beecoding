import { createApp } from 'vue';
import { createPinia } from 'pinia';
import { router } from './router';
import App from './App.vue';
import './style.css';
import './lib/theme';
import { pruneDrafts } from './lib/draft';

pruneDrafts();

// Monaco: use its bundled workers via Vite ?worker imports (no CDN).
import editorWorker from 'monaco-editor/esm/vs/editor/editor.worker?worker';
import tsWorker from 'monaco-editor/esm/vs/language/typescript/ts.worker?worker';

self.MonacoEnvironment = {
  getWorker(_, label) {
    return label === 'typescript' || label === 'javascript' ? new tsWorker() : new editorWorker();
  },
};

createApp(App).use(createPinia()).use(router).mount('#app');
