// OrderOrange admin — notification service worker.
// Exists ONLY so Android Chrome can show system notifications (page-context
// `new Notification` throws there). Desktop uses it too when available, so
// clicking a notification focuses the portal and jumps to the right page.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(clients.claim()));

self.addEventListener('notificationclick', (e) => {
    e.notification.close();
    const url = (e.notification.data && e.notification.data.url) || '/';
    e.waitUntil(clients.matchAll({ type: 'window', includeUncontrolled: true }).then((list) => {
        for (const c of list) {
            if ('focus' in c) { c.navigate(url); return c.focus(); }
        }
        return clients.openWindow(url);
    }));
});

// Real Web Push: the server rings this worker even when no portal tab is open.
self.addEventListener('push', (e) => {
    let d = { title: 'OrderOrange', body: '', url: '/' };
    try { d = Object.assign(d, e.data.json()); } catch { }
    e.waitUntil(self.registration.showNotification(d.title, {
        body: d.body,
        icon: '/_content/OrderOrange.ClientCore/logo.svg',
        badge: '/_content/OrderOrange.ClientCore/logo.svg',
        data: { url: d.url },
    }));
});
