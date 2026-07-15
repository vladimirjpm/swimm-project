import { useCallback, useEffect, useSyncExternalStore } from 'react';

// ── Types ────────────────────────────────────────────────────────────────────

export interface AuthUser {
  isAuthenticated: boolean;
  userId: number | null;
  email: string | null;
  displayName: string | null;
  avatarUrl: string | null;
  roles: string[];
  isAdmin: boolean;
  swimmerId: number | null;
}

const GUEST: AuthUser = {
  isAuthenticated: false,
  userId: null,
  email: null,
  displayName: null,
  avatarUrl: null,
  roles: [],
  isAdmin: false,
  swimmerId: null,
};

// ── Модульный кэш на страницу ────────────────────────────────────────────────
// Один запрос /auth/me на загрузку страницы, сколько бы компонентов ни звало
// useAuth (шапка + favorites + hub-groups). refresh() обновляет всех подписчиков.

let cached: AuthUser | null = null; // null = ещё не грузили
let inflight: Promise<AuthUser> | null = null;
const listeners = new Set<() => void>();

function notify() {
  listeners.forEach((l) => l());
}

async function fetchMe(): Promise<AuthUser> {
  try {
    const r = await fetch('/auth/me', { credentials: 'include' });
    // 503 (БД недоступна) и прочие не-ok трактуем как «гость», не как ошибку рендера.
    if (!r.ok) return GUEST;
    const me = await r.json();
    if (!me?.isAuthenticated) return GUEST;
    const roles: string[] = Array.isArray(me.roles) ? me.roles : [];
    return {
      isAuthenticated: true,
      userId: me.id ?? null,
      email: me.email ?? null,
      displayName: me.displayName ?? null,
      avatarUrl: me.avatarUrl ?? null,
      roles,
      isAdmin: roles.includes('Admin'),
      swimmerId: me.swimmerId ?? null,
    };
  } catch {
    return GUEST;
  }
}

/**
 * Текущий пользователь как промис (для не-хукового кода, напр. init в useFavorites).
 * Кэшируется на страницу; force=true перечитывает /auth/me и оповещает подписчиков useAuth.
 */
export function getCurrentUser(force = false): Promise<AuthUser> {
  if (!force && cached) return Promise.resolve(cached);
  if (!force && inflight) return inflight;
  inflight = fetchMe().then((me) => {
    cached = me;
    inflight = null;
    notify();
    return me;
  });
  return inflight;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function getSnapshot(): AuthUser | null {
  return cached;
}

// ── Hook ─────────────────────────────────────────────────────────────────────

/**
 * Общий auth-хук клиента: единая точка чтения /auth/me (фаза 4.1).
 * `loading` — пока первый запрос страницы не завершён; до и после ошибки — гость.
 */
export function useAuth() {
  const user = useSyncExternalStore(subscribe, getSnapshot);

  useEffect(() => {
    getCurrentUser();
  }, []);

  const refresh = useCallback(() => getCurrentUser(true), []);

  return { ...(user ?? GUEST), loading: user === null, refresh };
}
