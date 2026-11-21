import { defineStore } from 'pinia';

// "New badge" toast: one at a time, like the other global toasts; several badges at once are listed together.
export const useBadgeToast = defineStore('badgeToast', {
  state: () => ({ visible: false, badges: [], deadline: 0 }),
  actions: {
    show(badges, seconds = 7) {
      this.badges = badges;
      this.deadline = Date.now() + seconds * 1000;
      this.visible = true;
    },
    dismiss() { this.visible = false; },
  },
});
