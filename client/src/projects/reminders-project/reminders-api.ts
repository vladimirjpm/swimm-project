import { fetchAntiforgeryToken, invalidateTokenCache } from '../../utils/antiforgery';

/**
 * Серверная часть напоминаний (docs/plans/web-push-reminders-plan.md §6): подписки,
 * «Send test», настройки. Всё — про свой аккаунт; мутации с antiforgery.
 */

export type ReminderLead = '1h' | '2h' | 'evening';

export interface PushDevice {
  id: number;
  label: string;
  created_at: string;
  last_success_at: string | null;
  last_failure_at: string | null;
  is_current: boolean;
}

export interface NotificationSettings {
  reminders_enabled: boolean;
  lead: ReminderLead;
  email_fallback: boolean;
  muted_group_ids: number[];
  push_configured: boolean;
  devices: PushDevice[];
}

export interface PushTestResult {
  devices: number;
  delivered: number;
  removed: number;
  failed: number;
}

/** Адрес подписки — в заголовке, не в URL: по нему можно слать push человеку. */
const ENDPOINT_HEADER = 'X-Push-Endpoint';

async function call(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown): Promise<Response> {
  const token = await fetchAntiforgeryToken();
  const init: RequestInit = {
    method,
    credentials: 'include',
    headers: {
      ...(token ? { 'X-XSRF-TOKEN': token } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  };
  let r = await fetch(path, init);
  if (r.status === 400 || r.status === 403) {
    // Протухший токен (новая сессия) — один повтор со свежим.
    invalidateTokenCache();
    const fresh = await fetchAntiforgeryToken();
    if (fresh && fresh !== token) r = await fetch(path, { ...init, headers: { ...init.headers, 'X-XSRF-TOKEN': fresh } });
  }
  return r;
}

async function errorOf(r: Response): Promise<string | null> {
  if (r.ok) return null;
  if (r.status === 429) return 'Too many attempts — try again in a minute.';
  try {
    const data = await r.json();
    if (typeof data?.error === 'string') return data.error;
  } catch {
    // тело не JSON
  }
  return 'Something went wrong — please try again.';
}

export async function getVapidPublicKey(): Promise<string | null> {
  const r = await fetch('/api/push/vapid-public-key', { credentials: 'include' });
  if (!r.ok) return null;
  return (await r.json()).public_key ?? null;
}

export async function getSettings(currentEndpoint: string | null): Promise<NotificationSettings | null> {
  const r = await fetch('/api/me/notification-settings', {
    credentials: 'include',
    headers: currentEndpoint ? { [ENDPOINT_HEADER]: currentEndpoint } : {},
  });
  return r.ok ? r.json() : null;
}

export async function saveSettings(patch: Partial<Pick<NotificationSettings, 'reminders_enabled' | 'lead' | 'email_fallback'>>): Promise<string | null> {
  return errorOf(await call('PUT', '/api/me/notification-settings', patch));
}

export async function saveSubscription(sub: PushSubscription): Promise<string | null> {
  return errorOf(await call('POST', '/api/push/subscriptions', sub.toJSON()));
}

export async function deleteSubscription(endpoint: string): Promise<string | null> {
  return errorOf(await call('DELETE', '/api/push/subscriptions', { endpoint }));
}

export async function removeDevice(id: number): Promise<string | null> {
  return errorOf(await call('DELETE', `/api/push/devices/${id}`));
}

export async function sendTest(): Promise<{ result: PushTestResult | null; error: string | null }> {
  const r = await call('POST', '/api/push/test');
  const error = await errorOf(r);
  return { result: error ? null : await r.json(), error };
}
