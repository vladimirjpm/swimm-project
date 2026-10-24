import { fetchAntiforgeryToken, invalidateTokenCache } from '../../utils/antiforgery';
import type { HubGroupDeleteImpact } from '../hub-groups-project/my-groups-types';

/**
 * Удаление своего аккаунта (docs/plans/account-deletion-plan.md, У3): `GET/DELETE /api/me/account`.
 * Правило «можно ли» и подтверждение решает сервер (`AccountDeletionRules`) — здесь только вызовы.
 */

export interface AccountSharedGroup {
  id: number;
  name: string;
  nameEn: string | null;
  slug: string;
  /** Другие аккаунты в группе: участники и админы, кроме владельца. */
  otherPeople: number;
}

export interface AccountDeletionImpact {
  userId: number;
  favorites: number;
  media: number;
  publishedMedia: number;
  reactions: number;
  startListPlans: number;
  groupMemberships: number;
  groupAdminships: number;
  pushDevices: number;
  /** Добавленное в чужие группы — остаётся там без автора (Р4). */
  groupContentKept: number;
  /** Свои группы без других людей — уходят вместе с аккаунтом. */
  groupsDeleted: HubGroupDeleteImpact[];
  /** Свои группы с другими людьми — блокируют удаление (Р2). */
  sharedGroups: AccountSharedGroup[];
  hasPassword: boolean;
  signInFresh: boolean;
  blockReasons: string[];
  blockMessages: string[];
}

export async function fetchDeletionImpact(): Promise<AccountDeletionImpact | null> {
  try {
    const r = await fetch('/api/me/account/deletion-impact', { credentials: 'include' });
    return r.ok ? ((await r.json()) as AccountDeletionImpact) : null;
  } catch {
    return null;
  }
}

/** null — удалён; иначе текст ошибки для окна. */
export async function deleteAccount(confirmEmail: string, password: string | null): Promise<string | null> {
  const send = (token: string | null) => fetch('/api/me/account', {
    method: 'DELETE',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(token ? { 'X-XSRF-TOKEN': token } : {}) },
    body: JSON.stringify({ confirmEmail, password }),
  });
  try {
    const token = await fetchAntiforgeryToken();
    let r = await send(token);
    // Протухший антифоржери-токен — 400; повтор только тогда. 403 здесь — неверный пароль или
    // email: повторять его нельзя, каждая попытка идёт в лимит входа.
    if (r.status === 400) {
      invalidateTokenCache();
      const fresh = await fetchAntiforgeryToken();
      if (fresh && fresh !== token) r = await send(fresh);
    }
    if (r.ok) return null;
    if (r.status === 429) return 'Too many attempts — please wait a minute and try again.';
    const body = await r.json().catch(() => null);
    return (body && typeof body.error === 'string' && body.error) || 'Could not delete the account — please try again.';
  } catch {
    return 'Could not delete the account — please try again.';
  }
}
