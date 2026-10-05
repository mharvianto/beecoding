import { defineStore } from 'pinia';
import { api } from '../lib/api';

// How many "this problem is wrong" reports are waiting for the signed-in teacher / admin (for the badge in the user menu).
export const useProblemReports = defineStore('problemReports', {
  state: () => ({ open: 0 }),
  actions: {
    async refresh() {
      try { this.open = (await api.get('/api/problem-reports?status=Open&pageSize=1')).openTotal || 0; } catch { this.open = 0; }
    },
  },
});
