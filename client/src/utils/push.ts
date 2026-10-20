/**
 * Браузерная часть Web Push (docs/plans/web-push-reminders-plan.md §8): умеет ли браузер push,
 * регистрация service worker'а `/sw.js` и подписка в `PushManager`. Сервер здесь не вызывается —
 * это делает `reminders-api.ts`.
 *
 * Service worker один на весь сайт (scope `/`): зарегистрированный со страницы Reminders, он
 * получает push на любой странице и при закрытой вкладке. Поэтому регистрации в каждой точке
 * входа multi-page сборки не нужно — только там, где человек включает напоминания.
 */

export type PushSupport =
  /** Можно включать. */
  | 'supported'
  /** iPhone / iPad в обычной вкладке Safari: push есть только у сайта на экране «Домой» (iOS 16.4+). */
  | 'ios-needs-home-screen'
  /** Браузер push не умеет. */
  | 'unsupported';

function isIos(): boolean {
  const ua = navigator.userAgent;
  // iPadOS 13+ представляется Mac'ом — отличаем по сенсорному экрану.
  return /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
}

function isStandalone(): boolean {
  return window.matchMedia?.('(display-mode: standalone)').matches
    || (navigator as Navigator & { standalone?: boolean }).standalone === true;
}

export function pushSupport(): PushSupport {
  const capable = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
  if (isIos() && !isStandalone()) return 'ios-needs-home-screen';
  return capable ? 'supported' : 'unsupported';
}

/** Разрешение на уведомления: granted / denied / default (ещё не спрашивали). */
export function notificationPermission(): NotificationPermission | null {
  return 'Notification' in window ? Notification.permission : null;
}

/** Текущая подписка этого браузера; null — не подписан (или service worker'а нет). */
export async function currentSubscription(): Promise<PushSubscription | null> {
  if (!('serviceWorker' in navigator)) return null;
  const reg = await navigator.serviceWorker.getRegistration('/');
  return reg ? reg.pushManager.getSubscription() : null;
}

function keyBytes(base64Url: string): Uint8Array {
  const pad = '='.repeat((4 - (base64Url.length % 4)) % 4);
  const raw = atob((base64Url + pad).replace(/-/g, '+').replace(/_/g, '/'));
  const out = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
  return out;
}

/**
 * Включить push в этом браузере: спросить разрешение (⚠ только из обработчика нажатия — iOS
 * иначе не спросит), зарегистрировать `/sw.js`, подписаться ключом сервера. Подписка, сделанная
 * другим ключом (сменили VAPID), пересоздаётся. Отказ в разрешении — ошибка `denied`.
 */
export async function enablePush(publicKey: string): Promise<PushSubscription> {
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') throw new Error(permission === 'denied' ? 'denied' : 'dismissed');

  await navigator.serviceWorker.register('/sw.js', { scope: '/' });
  const reg = await navigator.serviceWorker.ready;
  const key = keyBytes(publicKey);
  const options = { userVisibleOnly: true, applicationServerKey: key };
  try {
    return await reg.pushManager.subscribe(options);
  } catch (e) {
    // Старая подписка с другим ключом: InvalidStateError — снять и подписаться заново.
    const old = await reg.pushManager.getSubscription();
    if (!old) throw e;
    await old.unsubscribe();
    return reg.pushManager.subscribe(options);
  }
}

/** Снять подписку в этом браузере; вернуть её адрес, чтобы сервер удалил строку. */
export async function disablePush(): Promise<string | null> {
  const sub = await currentSubscription();
  if (!sub) return null;
  const endpoint = sub.endpoint;
  await sub.unsubscribe();
  return endpoint;
}
