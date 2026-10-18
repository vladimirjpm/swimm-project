import { fetchAntiforgeryToken, invalidateTokenCache } from '../../utils/antiforgery';

/**
 * Записи из попапа «Set up profile» (хендофф ex4, 5d) — прямыми вызовами API, без
 * `useFavorites`. Причина — Undo: его замыкание выполняется через несколько секунд, когда
 * снимок хука уже устарел (переключатели `toggle*` решают по нему, добавить или снять).
 * Здесь каждая функция делает ровно одно и знает только id; показывает страница статус с
 * сервера (`useProfileSetup().refresh()`).
 *
 * Возврат — текст ошибки (лимиты — текстом сервера) или null при успехе.
 */

const SAVE_FAILED = 'Could not save — please try again.';

async function call(
  method: 'POST' | 'DELETE', path: string, body?: unknown, keepalive = false,
): Promise<Response> {
  const token = await fetchAntiforgeryToken();
  const init: RequestInit = {
    method,
    credentials: 'include',
    keepalive,
    headers: {
      ...(token ? { 'X-XSRF-TOKEN': token } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  };
  let r = await fetch(path, init);
  // Протухший токен (сессия сменилась) — один повтор со свежим.
  if (r.status === 400 || r.status === 403) {
    invalidateTokenCache();
    const fresh = await fetchAntiforgeryToken();
    if (fresh && fresh !== token) r = await fetch(path, { ...init, headers: { ...init.headers, 'X-XSRF-TOKEN': fresh } });
  }
  return r;
}

async function errorOf(r: Response): Promise<string | null> {
  if (r.ok) return null;
  const body = await r.json().catch(() => null);
  return (body && typeof body.error === 'string' && body.error) || SAVE_FAILED;
}

interface FavoriteRow { id: number; target_type: string; swimmer_id: number | null; club_id: number | null }

/**
 * Запись избранного для пловца/клуба: есть — её id, нет — добавить. `created` — добавили сейчас
 * (Undo тогда её удаляет; уже бывшую — не трогает). 409 «уже есть» приходит без id — берём из списка.
 */
export async function ensureFavorite(
  type: 'swimmer' | 'club', targetId: number,
): Promise<{ id: number; created: boolean } | { error: string }> {
  const r = await call('POST', '/api/me/favorites',
    type === 'club' ? { target_type: 'club', club_id: targetId } : { target_type: 'swimmer', swimmer_id: targetId });
  if (r.ok) {
    const fav = await r.json() as FavoriteRow;
    return { id: fav.id, created: true };
  }
  if (r.status === 409) {
    const list = await fetch('/api/me/favorites', { credentials: 'include', cache: 'no-store' })
      .then((x) => (x.ok ? x.json() as Promise<FavoriteRow[]> : []));
    const found = list.find((f) => f.target_type === type && (type === 'club' ? f.club_id : f.swimmer_id) === targetId);
    return found ? { id: found.id, created: false } : { error: SAVE_FAILED };
  }
  return { error: (await errorOf(r)) ?? SAVE_FAILED };
}

export const removeFavorite = async (favoriteId: number) =>
  errorOf(await call('DELETE', `/api/me/favorites/${favoriteId}`));

/** «Это я»: поставить звезду (сервер снимает её с прежнего) или снять. */
export const setPrimary = async (favoriteId: number, on: boolean) =>
  errorOf(await call(on ? 'POST' : 'DELETE', `/api/me/favorites/${favoriteId}/primary`));

/** Семья: золотое сердечко. Лимит семьи — 422 с текстом сервера. */
export const setFamily = async (favoriteId: number, on: boolean) =>
  errorOf(await call(on ? 'POST' : 'DELETE', `/api/me/favorites/${favoriteId}/family`));

/** Вступить в группу (у группы с одобрением — заявка, pending). */
export const joinGroup = async (groupId: number) =>
  errorOf(await call('POST', `/api/me/hub-groups/${groupId}/join`));

/**
 * Выйти из группы (заявка в ожидании отменяется). `keepalive` — для ухода, отложенного под Undo
 * (Р9): если вкладку закрыли раньше, запрос всё равно должен дойти.
 */
export const leaveGroup = async (groupId: number, keepalive = false) =>
  errorOf(await call('DELETE', `/api/me/hub-groups/${groupId}/join`, undefined, keepalive));
