import { defineStore } from 'pinia';
import { api } from '../lib/api';

export const useAuth = defineStore('auth', {
  state: () => ({
    user: null,
    ready: false,
    // public server capabilities: { passwordReset, emailVerification, requireVerifiedEmail }
    config: { passwordReset: false, emailVerification: false, requireVerifiedEmail: false },
    configLoaded: false,
  }),
  getters: {
    isTeacher: (s) => s.user?.role === 'Teacher',
    // signed in, the server can send mail, and this account's address isn't confirmed yet
    needsVerification: (s) => !!s.user && s.config.emailVerification && s.user.emailVerified === false,
    // ...and the server locks unverified accounts to the verification screen
    mustVerify() { return this.needsVerification && this.config.requireVerifiedEmail && !this.user.isAdmin; },
  },
  actions: {
    async fetchMe() {
      try {
        this.user = await api.get('/api/auth/me');
      } catch {
        this.user = null;
      } finally {
        this.ready = true;
      }
    },
    async loadConfig() {
      if (this.configLoaded) return;
      try { this.config = await api.get('/api/auth/config'); } catch { /* keep the defaults (features off) */ }
      this.configLoaded = true;
    },
    async login(email, password) {
      this.user = await api.post('/api/auth/login', { email, password });
    },
    async register(payload) {
      this.user = await api.post('/api/auth/register', payload);
    },
    async logout() {
      await api.post('/api/auth/logout');
      this.user = null;
    },
    async changePassword(currentPassword, newPassword) {
      await api.post('/api/auth/change-password', { currentPassword, newPassword });
    },
    async updateDisplayName(displayName) {
      this.user = await api.patch('/api/auth/profile', { displayName });
    },
    async deleteAccount(password, deleteOwnedBoards = false) {
      await api.del('/api/auth/account', { password, deleteOwnedBoards });
      this.user = null;
    },
  },
});
