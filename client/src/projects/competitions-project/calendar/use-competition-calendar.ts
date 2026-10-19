import React from 'react';
import type { CompetitionCalendar } from './calendar-types';
import { meetDisplayName } from './calendar-model';

/*
 * Загрузка сезона календаря: GET /api/competitions/calendar?season=<год начала>.
 * Ответы кэшируются в модуле по сезону — повторный заход в сезон идёт без запроса.
 * Устаревший ответ не перетирает свежий: кэш ключится сезоном, а перерисовку запрашивает
 * только эффект, который ещё не отменён.
 */

const cache = new Map<number, CompetitionCalendar>();

export interface CompetitionCalendarResult {
  /** null — ещё не пришло (или упало). */
  data: CompetitionCalendar | null;
  loading: boolean;
  error: boolean;
  /** Повторить запрос после ошибки (кэш ошибку не хранит). */
  retry: () => void;
}

export function useCompetitionCalendar(season: number): CompetitionCalendarResult {
  const [, bump] = React.useState(0);
  const [failedSeason, setFailedSeason] = React.useState<number | null>(null);
  const [attempt, setAttempt] = React.useState(0);

  React.useEffect(() => {
    if (cache.has(season)) return;
    let cancelled = false;
    setFailedSeason(null);
    (async () => {
      try {
        const resp = await fetch(`/api/competitions/calendar?season=${season}`, { credentials: 'same-origin' });
        if (!resp.ok) throw new Error(`API /api/competitions/calendar returned ${resp.status}`);
        const body = (await resp.json()) as CompetitionCalendar;
        // Имя чистится один раз здесь: так его видят все виды, aria-label и сортировка Meet days.
        cache.set(season, { ...body, meets: body.meets.map((m) => ({ ...m, name: meetDisplayName(m.name) })) });
        if (!cancelled) bump((n) => n + 1);
      } catch (e) {
        console.error('Error loading /api/competitions/calendar', e);
        if (!cancelled) setFailedSeason(season);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [season, attempt]);

  const data = cache.get(season) ?? null;
  const error = !data && failedSeason === season;
  const retry = React.useCallback(() => setAttempt((n) => n + 1), []);
  return { data, loading: !data && !error, error, retry };
}
