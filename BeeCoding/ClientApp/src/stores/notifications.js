import { defineStore } from 'pinia';
import { api } from '../lib/api';
import { createBoardConnection } from '../lib/signalr';

const TOAST_MS = 6000;

// In-app notifications (reactions/comments on my wall post): the navbar bell's list + unread
// count, plus live toasts. A dedicated, board-less hub connection stays open for the logged-in
// user so a notification arrives wherever they are in the app. `suppressToast` lets App.vue
// skip the toast when the user is already looking at the board it's about.
export const useNotifications = defineStore('notifications', {
  state: () => ({
    items: [],
    unread: 0,
    toasts: [],
    conn: null,
    suppressToast: null,   // (item) => boolean
  }),
  actions: {
    async load() {
      try {
        const r = await api.get('/api/notifications');
        this.items = r.items;
        this.unread = r.unread;
      } catch { /* the bell is best-effort */ }
    },

    async start() {
      if (this.conn) return;
      await this.load();
      const conn = createBoardConnection();
      conn.on('notification', (e) => this.ingest(e));
      conn.on('notificationRemoved', (e) => this.remove(e));
      conn.onreconnected(() => this.load());   // anything missed while disconnected
      this.conn = conn;
      try { await conn.start(); } catch { /* realtime is best-effort; the bell still loads on refresh */ }
    },

    async stop() {
      const conn = this.conn;
      this.conn = null;
      this.items = []; this.unread = 0; this.toasts = [];
      try { await conn?.stop(); } catch { /* ignore */ }
    },

    ingest({ item, unread }) {
      this.items = [item, ...this.items.filter((n) => n.id !== item.id)].slice(0, 30);
      this.unread = unread;
      if (this.suppressToast?.(item)) return;
      this.toasts = [item, ...this.toasts.filter((t) => t.id !== item.id)].slice(0, 3);
      setTimeout(() => this.dismissToast(item.id), TOAST_MS);
    },

    remove({ id, unread }) {
      this.items = this.items.filter((n) => n.id !== id);
      this.toasts = this.toasts.filter((t) => t.id !== id);
      this.unread = unread;
    },

    dismissToast(id) { this.toasts = this.toasts.filter((t) => t.id !== id); },

    async markRead(n) {
      if (n.read) return;
      n.read = true;
      this.unread = Math.max(0, this.unread - 1);
      try { this.unread = (await api.post(`/api/notifications/${n.id}/read`)).unread; } catch { /* ignore */ }
    },

    async markAllRead() {
      this.items.forEach((n) => { n.read = true; });
      this.unread = 0;
      try { await api.post('/api/notifications/read-all'); } catch { /* ignore */ }
    },
  },
});

/** One-line description shared by the bell list and the toast. */
export function notificationText(n) {
  const who = n.actorIsStaff ? `${n.actorName} 👨‍🏫` : n.actorName;
  if (n.kind === 'comment') return `${who} commented: “${n.snippet}”`;
  return n.count > 1
    ? `${n.count} new reactions on your card (latest ${n.emoji} from ${who})`
    : `${who} reacted ${n.emoji} to your card`;
}
