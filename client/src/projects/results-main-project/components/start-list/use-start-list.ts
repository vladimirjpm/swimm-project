import { useCallback, useEffect, useState } from 'react';
import type {
  StartListClub, StartListEventHeats, StartListProgramme, StartListSwim, StartListSwimmer,
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

/**
 * Программы ВСЕХ источников соревнования (Т6). У составного старта дни лежат по разным
 * протоколам: окружные чемпионаты — это 15/02, 16/02 и 19/02 в четырёх compID, и карточка
 * плана, спросив программу только у первого, показала бы один день из трёх.
 *
 * Отсюда же придёт время разминки по дням для ARRIVE BY (Т8) — оно тоже привязано к дню
 * конкретного протокола.
 */
export function useStartListProgrammes(orgCompIds: number[]) {
  const [data, setData] = useState<StartListProgramme[]>([]);
  const [loading, setLoading] = useState(false);
  const [tick, setTick] = useState(0);

  const key = [...orgCompIds].sort((a, b) => a - b).join(',');

  useEffect(() => {
    const ids = key ? key.split(',').map(Number) : [];
    if (ids.length === 0) { setData([]); return; }

    let cancelled = false;
    setLoading(true);

    (async () => {
      const parts = await Promise.all(ids.map(async (id) => {
        try {
          const r = await fetch(`/api/start-list/${id}`, { credentials: 'same-origin' });
          return r.ok ? ((await r.json()) as StartListProgramme) : null;
        } catch {
          return null;
        }
      }));
      if (cancelled) return;
      setData(parts.filter((p): p is StartListProgramme => p !== null));
      setLoading(false);
    })();

    return () => { cancelled = true; };
  }, [key, tick]);

  const refresh = useCallback(() => setTick((t) => t + 1), []);
  return { data, loading, refresh };
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

/** Клубы соревнования со счётчиками — секция «follow a whole club» пикера (Т2). */
export function useStartListClubs(orgCompIds: number[]) {
  const url = orgCompIds.length > 0
    ? `/api/start-list/clubs?${[...orgCompIds].sort((a, b) => a - b).map((id) => `orgCompId=${id}`).join('&')}`
    : null;
  return useJson<StartListClub[]>(url);
}

/**
 * Карточки СРАЗУ НЕСКОЛЬКИХ пловцов — состав личного плана (Т5/Т6): и пикеру («в какие дни
 * плывёт этот избранный»), и карточке плана (все заплывы выбранных одним списком).
 *
 * Запрос на каждого, а не один пакетный: своего эндпоинта под список пловцов нет, а состав
 * плана — это единицы человек. Заводить его ради экономии двух-трёх запросов рано; если
 * список начнёт расти (клуб целиком идёт ДРУГИМ путём, через `clubs/{id}`), это первое
 * место, куда смотреть.
 *
 * Пловец без заявок (404) в выдачу просто не попадает — он мог сняться после того, как его
 * добавили в план.
 */
export function useStartListSwimmers(orgCompIds: number[], swimmerIds: number[]) {
  const [data, setData] = useState<Record<number, StartListSwimmer>>({});
  const [loading, setLoading] = useState(false);
  const [tick, setTick] = useState(0);

  // Ключи строкой: массивы в зависимостях эффекта меняются каждым рендером.
  const idsKey = [...swimmerIds].sort((a, b) => a - b).join(',');
  const sourcesKey = [...orgCompIds].sort((a, b) => a - b).join(',');

  useEffect(() => {
    const ids = idsKey ? idsKey.split(',').map(Number) : [];
    const sources = sourcesKey ? sourcesKey.split(',').map(Number) : [];
    if (ids.length === 0 || sources.length === 0) { setData({}); return; }

    let cancelled = false;
    setLoading(true);
    const query = sources.map((id) => `orgCompId=${id}`).join('&');

    (async () => {
      const pairs = await Promise.all(ids.map(async (id) => {
        try {
          const r = await fetch(`/api/start-list/swimmers/${id}?${query}`, { credentials: 'same-origin' });
          if (!r.ok) return null;
          return [id, (await r.json()) as StartListSwimmer] as const;
        } catch {
          return null;
        }
      }));
      if (cancelled) return;
      setData(Object.fromEntries(pairs.filter((p): p is NonNullable<typeof p> => p !== null)));
      setLoading(false);
    })();

    return () => { cancelled = true; };
  }, [idsKey, sourcesKey, tick]);

  const refresh = useCallback(() => setTick((t) => t + 1), []);
  return { data, loading, refresh };
}

/**
 * Заплывы выбранных КЛУБОВ целиком (Т6): второй источник строк карточки плана — рядом с
 * заплывами выбранных пловцов.
 *
 * Запросов — клубы × источники: срез клуба живёт внутри одного протокола
 * (`{orgCompId}/clubs/{clubId}`), а составной старт собран из нескольких. И того, и другого
 * единицы, поэтому крест дешёвый.
 */
export function useStartListClubSwims(orgCompIds: number[], clubIds: number[]) {
  const [data, setData] = useState<StartListSwim[]>([]);
  const [loading, setLoading] = useState(false);

  const clubsKey = [...clubIds].sort((a, b) => a - b).join(',');
  const sourcesKey = [...orgCompIds].sort((a, b) => a - b).join(',');

  useEffect(() => {
    const clubs = clubsKey ? clubsKey.split(',').map(Number) : [];
    const sources = sourcesKey ? sourcesKey.split(',').map(Number) : [];
    if (clubs.length === 0 || sources.length === 0) { setData([]); return; }

    let cancelled = false;
    setLoading(true);

    (async () => {
      const chunks = await Promise.all(
        sources.flatMap((src) => clubs.map(async (clubId) => {
          try {
            const r = await fetch(`/api/start-list/${src}/clubs/${clubId}`, { credentials: 'same-origin' });
            return r.ok ? ((await r.json()) as StartListSwim[]) : [];
          } catch {
            return [];
          }
        })),
      );
      if (cancelled) return;
      setData(chunks.flat());
      setLoading(false);
    })();

    return () => { cancelled = true; };
  }, [clubsKey, sourcesKey]);

  return { data, loading };
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
