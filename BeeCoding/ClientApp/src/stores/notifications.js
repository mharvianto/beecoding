import { defineStore } from 'pinia';
import { useProblemReports } from './problemReports';
import { api } from '../lib/api';
import { createBoardConnection } from '../lib/signalr';
import { showBrowserNotification } from '../lib/browserNotify';

const TOAST_MS = 6000;

// In-app notifications (reactions/comments on my wall post): the navbar bell's list + unread
// count, plus live toasts. A dedicated, board-less hub connection stays open for the logged-in
// user so a notification arrives wherever they are in the app. `suppressToast` lets App.vue
// skip the toast when the user is already looking at the board it's about.
export const useNotifications = defineStore('notifications', {
  state: () => ({
    items: [],
    unread: 0,
    total: 0,              // everything the server holds for me — the list is paged
    toasts: [],
    conn: null,
    suppressToast: null,   // (item) => boolean
    onMemberRemoved: null, // ({ boardId, boardSlug, boardTitle }) => void — set by App.vue
    onBrowserClick: null,  // (item) => void — opens the notification's target; set by App.vue
  }),
  actions: {
    async load() {
      try {
        const r = await api.get('/api/notifications');
        this.items = r.items;
        this.unread = r.unread;
        this.total = r.total;
      } catch { /* the bell is best-effort */ }
    },

    /** The next page of older notifications, appended to the list. */
    async loadMore() {
      try {
        const r = await api.get(`/api/notifications?skip=${this.items.length}`);
        const have = new Set(this.items.map((n) => n.id));
        this.items = [...this.items, ...r.items.filter((n) => !have.has(n.id))];
        this.unread = r.unread;
        this.total = r.total;
      } catch { /* the bell is best-effort */ }
    },

    /** Delete one notification I have already read. */
    async deleteRead(n) {
      if (!n.read) return;
      this.items = this.items.filter((x) => x.id !== n.id);
      this.total = Math.max(0, this.total - 1);
      try { await api.del(`/api/notifications/${n.id}`); } catch { await this.load(); }
    },

    /** "Clear read": drop every read notification. */
    async clearRead() {
      const gone = this.items.filter((n) => n.read).length;
      this.items = this.items.filter((n) => !n.read);
      this.total = Math.max(0, this.total - gone);
      try { await api.del('/api/notifications/read'); } catch { /* ignore */ }
      await this.load();   // the server may have had read ones beyond the loaded page
    },

    async start() {
      if (this.conn) return;
      await this.load();
      const conn = createBoardConnection();
      conn.on('notification', (e) => this.ingest(e));
      conn.on('notificationRemoved', (e) => this.remove(e));
      conn.on('memberRemoved', (e) => this.onMemberRemoved?.(e));
      conn.onreconnected(() => this.load());   // anything missed while disconnected
      this.conn = conn;
      try { await conn.start(); } catch { /* realtime is best-effort; the bell still loads on refresh */ }
    },

    async stop() {
      const conn = this.conn;
      this.conn = null;
      this.items = []; this.unread = 0; this.total = 0; this.toasts = [];
      try { await conn?.stop(); } catch { /* ignore */ }
    },

    ingest({ item, unread }) {
      if (item.kind === 'report') useProblemReports().refresh();   // keep the open-reports count in the user menu current
      const known = this.items.some((n) => n.id === item.id);
      this.items = [item, ...this.items.filter((n) => n.id !== item.id)];
      if (!known) this.total += 1;
      this.unread = unread;
      showBrowserNotification({ id: item.id, title: item.problemTitle || 'BeeCoding', body: notificationText(item) }, () => this.onBrowserClick?.(item));
      if (this.suppressToast?.(item)) return;
      this.toasts = [item, ...this.toasts.filter((t) => t.id !== item.id)].slice(0, 3);
      setTimeout(() => this.dismissToast(item.id), TOAST_MS);
    },

    remove({ id, unread }) {
      if (this.items.some((n) => n.id === id)) this.total = Math.max(0, this.total - 1);
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

/** Where a notification leads: report kinds carry their own page, wall ones open the card on its board. */
export function notificationTarget(n) {
  return n.link ? n.link : { path: `/boards/${n.boardSlug}`, query: { problem: String(n.problemId), post: String(n.postId) } };
}

/** One-line description shared by the bell list and the toast. */
export function notificationText(n) {
  if (n.kind === 'report') return `${n.actorName} reported a problem — ${n.snippet}`;
  if (n.kind === 'report-update') {
    const verdict = n.emoji === '✅' ? 'resolved' : 'dismissed';
    return `Your report was ${verdict} by ${n.actorName}${n.snippet ? `: “${n.snippet}”` : ''}`;
  }
  if (n.kind === 'review-submitted') return `${n.actorName} submitted “${n.problemTitle}” for review`;
  if (n.kind === 'proposal') return `${n.actorName} proposed a problem — ${n.problemTitle}${n.snippet ? `: “${n.snippet}”` : ''}`;
  if (n.kind === 'proposal-update' || n.kind === 'practice-review') {
    const what = n.kind === 'proposal-update' ? 'proposal' : 'practice submission';
    return `Your ${what} “${n.problemTitle}” was ${n.emoji === '✅' ? 'approved' : 'declined'} by ${n.actorName}${n.snippet ? `: “${n.snippet}”` : ''}`;
  }
  if (n.kind === "org-invite") return `${n.actorName} invited you to the organization “${n.problemTitle}”`;
  if (n.kind === "group-opens") return `The session “${n.problemTitle}” on ${n.snippet} is now open`;
  if (n.kind === "group-closing") return `The session “${n.problemTitle}” on ${n.snippet} closes within the hour`;
  if (n.kind === "problem-new") return n.count > 1 ? `${n.count} new problems on ${n.snippet} (latest: ${n.problemTitle})` : `New problem on ${n.snippet}: ${n.problemTitle}`;
  if (n.kind === "lti-sync-failed") return n.count > 1 ? `Sending grades to the LMS failed ${n.count} times for “${n.problemTitle}”` : `Sending grades to the LMS failed for “${n.problemTitle}”`;
  if (n.kind === "security") return `Security: ${n.problemTitle}`;
  const who = n.actorIsStaff ? `${n.actorName} 👨‍🏫` : n.actorName;
  if (n.kind === 'comment')
    return n.count > 1
      ? `${n.count} new comments on your card (latest from ${who}: “${n.snippet}”)`
      : `${who} commented: “${n.snippet}”`;
  return n.count > 1
    ? `${n.count} new reactions on your card (latest ${n.emoji} from ${who})`
    : `${who} reacted ${n.emoji} to your card`;
}
