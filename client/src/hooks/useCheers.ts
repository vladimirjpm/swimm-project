import { useEffect, useSyncExternalStore } from 'react';
import { useAuth } from './useAuth';
import { fetchAntiforgeryToken, invalidateTokenCache } from '../utils/antiforgery';

/**
 * 🎉 «поздравить с заплывом» — общее хранилище счётчиков (этап 1, решение Влада 25.10.2026).
 *
 * Кнопка (`UI_CheerButton`) сама регистрирует свой resultId; хранилище копит их несколько
 * миллисекунд и спрашивает сервер ПАЧКОЙ (`GET /api/results/cheers?ids=…`). Поэтому ни таблице
 * протокола, ни списку пловца, ни группе не нужно собирать id заранее — кнопка ставится в строку,
 * и всё. Сервер отвечает только про заплывы, где поздравления есть: остальные — ноль.
 *
 * `mine` личный, поэтому кэш принадлежит пользователю: сменился вход — кэш сброшен и
 * перечитан. Кэш модульный (как токен в `utils/antiforgery.ts`): переживает перерисовки и
 * общий для всех строк страницы.
 */

export interface CheerState {
  count: number;
  mine: boolean;
}

const EMPTY: CheerState = { count: 0, mine: false };
/** Столько id уходит в один запрос (сервер принимает до 500). Зеркало `CheerRules.ClientChunk`:
 *  на нём посчитан лимит чтения по IP — поменял здесь, поменяй и там. Отказ по лимиту (429)
 *  просто оставляет кнопки без чисел. */
const CHUNK = 300;

const states = new Map<number, CheerState>();
const known = new Set<number>();
let pending = new Set<number>();
let timer: ReturnType<typeof setTimeout> | null = null;
let ownerKey: string | null = null;
const listeners = new Set<() => void>();

function notify() {
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Кэш принадлежит пользователю: другой вход — другие `mine`, всё перечитываем. */
function ensureOwner(key: string) {
  if (ownerKey === key) return;
  ownerKey = key;
  states.clear();
  known.clear();
  pending = new Set();
  notify();
}

async function flush() {
  timer = null;
  const ids = [...pending];
  pending = new Set();
  const owner = ownerKey;
  for (let i = 0; i < ids.length; i += CHUNK) {
    const chunk = ids.slice(i, i + CHUNK);
    try {
      const r = await fetch(`/api/results/cheers?ids=${chunk.join(',')}`, { credentials: 'include' });
      if (!r.ok) continue;
      const rows: Array<{ result_id: number; count: number; mine: boolean }> = await r.json();
      if (owner !== ownerKey) return; // пока ждали, сменился вход — ответ чужой
      for (const id of chunk) {
        known.add(id);
        states.delete(id);
      }
      for (const row of rows) states.set(row.result_id, { count: row.count, mine: row.mine });
      notify();
    } catch {
      /* сеть упала — кнопка просто без счётчика, следующий заход спросит снова */
    }
  }
}

function request(id: number, key: string) {
  ensureOwner(key);
  if (known.has(id) || pending.has(id)) return;
  pending.add(id);
  if (!timer) timer = setTimeout(flush, 20);
}

/**
 * Состояние 🎉 одного заплыва. Регистрирует id на чтение при монтировании и при смене входа.
 * `resultId` null — строка не из базы (статический источник): нули, сервер не спрашиваем.
 */
export function useCheer(resultId: number | null | undefined): CheerState {
  const { userId, loading } = useAuth();
  const key = loading ? null : String(userId ?? 'guest');

  useEffect(() => {
    if (resultId == null || key == null) return;
    request(resultId, key);
  }, [resultId, key]);

  return useSyncExternalStore(subscribe, () =>
    resultId == null ? EMPTY : states.get(resultId) ?? EMPTY,
  );
}

/**
 * Поставить/снять 🎉. Оптимистично: строка меняется сразу, ответ сервера её поправит, ошибка —
 * откатит. Возвращает false, если сервер отказал (неявка, пловец группы, не вошёл).
 */
export async function toggleCheer(resultId: number): Promise<boolean> {
  const before = states.get(resultId) ?? EMPTY;
  const on = !before.mine;
  states.set(resultId, { count: Math.max(0, before.count + (on ? 1 : -1)), mine: on });
  notify();

  const rollback = () => {
    states.set(resultId, before);
    notify();
    return false;
  };

  const token = await fetchAntiforgeryToken();
  if (!token) return rollback();
  try {
    const r = await fetch(`/api/results/${resultId}/cheer`, {
      method: on ? 'POST' : 'DELETE',
      credentials: 'include',
      headers: { 'X-XSRF-TOKEN': token },
    });
    if (!r.ok) {
      if (r.status === 400 || r.status === 403) invalidateTokenCache();
      return rollback();
    }
    const state: CheerState = await r.json();
    states.set(resultId, { count: state.count, mine: state.mine });
    notify();
    return true;
  } catch {
    return rollback();
  }
}
