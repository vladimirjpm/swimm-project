/*
 * Service worker SwimHub — только push-напоминания о тренировках
 * (docs/plans/web-push-reminders-plan.md §8). Кэша страниц и офлайна НЕТ — это не PWA-кэш,
 * сайт по-прежнему грузится из сети.
 *
 * Простой JS без сборки: файл лежит в client/public и отдаётся как /sw.js (scope «/»), сервер
 * шлёт его с Cache-Control: no-cache (Program.cs), чтобы правка доезжала до телефонов сразу.
 *
 * ⚠ iOS: каждый push ОБЯЗАН показать уведомление — «тихий» push Safari считает нарушением и
 * отзывает подписку. Поэтому showNotification вызывается всегда, даже на кривом сообщении.
 */

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('push', (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { body: event.data ? event.data.text() : '' };
  }

  const tag = typeof data.tag === 'string' && data.tag ? data.tag : undefined;
  event.waitUntil(
    self.registration.showNotification(data.title || 'SwimHub', {
      body: data.body || '',
      icon: '/logo192.png',
      badge: '/logo192.png',
      // Тег — ключ занятия: повтор заменяет уведомление, а не копит; renotify — всё равно звенеть.
      tag,
      renotify: Boolean(tag),
      dir: 'auto',
      data: { url: data.url || '/' },
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  // Только свой сайт: адрес приходит с нашего сервера, но открывать чужое окно повода нет.
  let url = new URL('/', self.location.origin);
  try {
    const target = new URL(event.notification.data?.url || '/', self.location.origin);
    if (target.origin === self.location.origin) url = target;
  } catch {
    // кривой адрес — открываем главную
  }

  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
      const same = windows.find((w) => w.url === url.href);
      if (same) return same.focus();
      const any = windows.find((w) => new URL(w.url).origin === self.location.origin);
      if (any && 'navigate' in any) {
        await any.navigate(url.href);
        return any.focus();
      }
      return self.clients.openWindow(url.href);
    })(),
  );
});
