import { defineStore } from 'pinia';

// A single global "Accepted!" toast for board + practice solves — same pattern as
// UndoToast, so it survives whatever page triggered it and only one shows at a time.
export const useCelebrationToast = defineStore('celebrationToast', {
  state: () => ({
    visible: false,
    xpGained: 0,
    level: 1,
    xp: 0,
    solvedToday: 0,
    deadline: 0,
  }),
  actions: {
    show({ xpGained = 0, level = 1, xp = 0, solvedToday = 1 }, seconds = 3.5) {
      this.xpGained = xpGained;
      this.level = level;
      this.xp = xp;
      this.solvedToday = solvedToday;
      this.deadline = Date.now() + seconds * 1000;
      this.visible = true;
    },
    dismiss() {
      this.visible = false;
    },
  },
});
