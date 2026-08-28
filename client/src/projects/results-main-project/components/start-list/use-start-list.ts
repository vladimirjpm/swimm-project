import { useCallback, useEffect, useState } from 'react';
import type {
  StartListEventHeats, StartListProgramme, StartListSwim, StartListSwimmer,
  StartListSwimmerHit, UpcomingCompetition,
} from './types';

// Все /api/start-list/* — анонимные, ETag + Cache-Control: max-age=30 (сервер). Автообновления
// нет (решение 7): страница обновляется только вручную по кнопке Refresh (см. `refresh()`).

interface FetchState<T> {
  data: T | null;
  loading: boolean;
  /** true — источник вернул 404: заявок на это соревнование ещё нет. */
  notFound: boolean;
  refresh: () => void;
}

function useJson<T>(url: string | null): FetchState<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    if (!url) { setData(null); setNotFound(false); return; }
    let cancelled = false;
    setLoading(true);
    (async () => {
      try {
        const r = await fetch(url, { credentials: 'same-origin' });
        if (cancelled) return;
        if (r.status === 404) { setNotFound(true); setData(null); return; }
        if (!r.ok) { setData(null); return; }
        const json = (await r.json()) as T;
        setNotFound(false);
        setData(json);
      } catch {
        if (!cancelled) setData(null);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url, tick]);

  const refresh = useCallback(() => setTick((t) => t + 1), []);
  return { data, loading, notFound, refresh };
}

/** Зум 1 — программа соревнования целиком. */
export function useStartListProgramme(orgCompId: number | null) {
  const url = orgCompId != null ? `/api/start-list/${orgCompId}` : null;
  return useJson<StartListProgramme>(url);
}

/** Зум 2 — заплывы одной дисциплины. */
export function useStartListEvent(orgCompId: number | null, orgDisciplineId: number | null) {
  const url = orgCompId != null && orgDisciplineId != null
    ? `/api/start-list/${orgCompId}/events/${orgDisciplineId}`
    : null;
  return useJson<StartListEventHeats>(url);
}

/** Зум 3 — карточка пловца на соревновании. */
export function useStartListSwimmer(orgCompId: number | null, swimmerId: number | null) {
  const url = orgCompId != null && swimmerId != null
    ? `/api/start-list/${orgCompId}/swimmers/${swimmerId}`
    : null;
  return useJson<StartListSwimmer>(url);
}

/** Поиск пловца по имени внутри соревнования — по ВСЕМ его источникам сразу.
 *  Запрос короче двух символов не шлём: сервер на него всё равно отдаёт пусто. */
export function useStartListSearch(orgCompIds: number[], query: string) {
  const q = query.trim();
  const url = q.length >= 2 && orgCompIds.length > 0
    ? `/api/start-list/search?${orgCompIds.map((id) => `orgCompId=${id}`).join('&')}&q=${encodeURIComponent(q)}`
    : null;
  return useJson<StartListSwimmerHit[]>(url);
}

/** Карточка пловца по всем источникам соревнования (зум 3 составного старта). */
export function useStartListSwimmerAcross(orgCompIds: number[], swimmerId: number | null) {
  const url = swimmerId != null && orgCompIds.length > 0
    ? `/api/start-list/swimmers/${swimmerId}?${orgCompIds.map((id) => `orgCompId=${id}`).join('&')}`
    : null;
  return useJson<StartListSwimmer>(url);
}

/** Секция «Upcoming» на /competitions (С7б). */
export function useUpcomingCompetitions(days = 60) {
  const url = `/api/start-list/competitions?days=${days}`;
  return useJson<UpcomingCompetition[]>(url);
}

/**
 * Ближайшие заплывы пловца/нескольких пловцов (страница пловца + «мои избранные», С8.3–4).
 * Пусто → массив [] (не 404), пустой список swimmerIds — запрос не шлём вовсе.
 */
export function useUpcomingStarts(swimmerIds: number[]) {
  const url = swimmerIds.length > 0
    ? `/api/start-list/upcoming?${[...swimmerIds].sort((a, b) => a - b).map((id) => `swimmerId=${id}`).join('&')}`
    : null;
  return useJson<StartListSwim[]>(url);
}
