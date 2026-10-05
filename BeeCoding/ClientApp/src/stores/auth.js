import { defineStore } from 'pinia';
import { api } from '../lib/api';
import { getPasskey } from '../lib/webauthn';

export const useAuth = defineStore('auth', {
  state: () => ({
    user: null,
    ready: false,
    // public server capabilities: { passwordReset, emailVerification, requireVerifiedEmail, passkeys }
    config: { passwordReset: false, emailVerification: false, requireVerifiedEmail: false, passkeys: false, google: false },
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
    // Resolves to null once signed in, or to { ticket, methods } when the account needs a second factor.
    async login(email, password) {
      const r = await api.post('/api/auth/login', { email, password });
      if (r.mfaRequired) return { ticket: r.ticket, methods: r.methods, emailHint: r.emailHint };
      this.user = r;
      return null;
    },
    // Google found an existing account for this email: prove it with the account's password to connect them.
    // Same result shape as login(): null once signed in, or { ticket, methods } when a second factor is needed.
    async googleLink(ticket, password) {
      const r = await api.post('/api/auth/google/link', { ticket, password });
      if (r.mfaRequired) return { ticket: r.ticket, methods: r.methods, emailHint: r.emailHint };
      this.user = r;
      return null;
    },
    async googleRegister(payload) {
      this.user = await api.post('/api/auth/google/register', payload);
    },
    async sendEmailCode(ticket) {
      await api.post('/api/auth/mfa/email/send', { ticket });
    },
    async verifyMfa(ticket, method, code) {
      this.user = await api.post('/api/auth/mfa/verify', { ticket, method, code });
    },
    // Passwordless: no email or password, the browser offers the passkeys it holds for this site.
    async loginWithPasskey() {
      const { options, state } = await api.post('/api/auth/passkeys/login/options');
      const response = await getPasskey(options);
      this.user = await api.post('/api/auth/passkeys/login/verify', { state, response });
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
