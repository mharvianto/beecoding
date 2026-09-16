import { defineStore } from 'pinia';
import { api } from '../lib/api';
import { localDayKey } from '../lib/localDay';

export const useProgress = defineStore('progress', {
  state: () => ({ xp: 0, level: 1, levelStartXp: 0, nextLevelXp: 50, solvedCount: 0, streak: 0, ready: false }),
  getters: {
    // 0..1 fill of the current level's bar
    pct: (s) => {
      const span = s.nextLevelXp - s.levelStartXp;
      return span > 0 ? Math.min(1, Math.max(0, (s.xp - s.levelStartXp) / span)) : 0;
    },
    toNext: (s) => Math.max(0, s.nextLevelXp - s.xp),
  },
  actions: {
    async refresh() {
      try {
        const p = await api.get(`/api/me/progress?localDay=${localDayKey()}`);
        this.$patch({ ...p, ready: true });
      } catch { /* not logged in yet */ }
    },
    reset() {
      this.$patch({ xp: 0, level: 1, levelStartXp: 0, nextLevelXp: 50, solvedCount: 0, streak: 0, ready: false });
    },
  },
});
