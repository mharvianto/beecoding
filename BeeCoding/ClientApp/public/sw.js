// Web Push for the bell: shows a notification when the server pushes one (also with the tab closed) and opens its target on click.
// The scope is the app's base path, so `url` (a router path such as /boards/x) is resolved against it.
self.addEventListener('push', (event) => {
  let d = {};
  try { d = event.data ? event.data.json() : {}; } catch { /* not JSON: show the defaults */ }
  event.waitUntil(self.registration.showNotification(d.title || 'BeeCoding', {
    body: d.body || '',
    tag: d.tag,
    icon: `${self.registration.scope}apple-touch-icon.png`,
    data: { url: d.url || '/' },
  }));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const path = (event.notification.data && event.notification.data.url) || '/';
  const base = self.registration.scope.replace(/\/$/, '');
  event.waitUntil((async () => {
    const wins = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const open = wins.find((c) => c.url.startsWith(base));
    if (open) {
      await open.focus();
      open.postMessage({ type: 'navigate', url: path });   // the SPA routes without a reload (App.vue)
    } else {
      await self.clients.openWindow(base + path);
    }
  })());
});
