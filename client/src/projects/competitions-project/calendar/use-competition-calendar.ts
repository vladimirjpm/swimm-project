import React from 'react';
import type { CompetitionCalendar } from './calendar-types';

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
}

export function useCompetitionCalendar(season: number): CompetitionCalendarResult {
  const [, bump] = React.useState(0);
  const [failedSeason, setFailedSeason] = React.useState<number | null>(null);

  React.useEffect(() => {
    if (cache.has(season)) return;
    let cancelled = false;
    setFailedSeason(null);
    (async () => {
      try {
        const resp = await fetch(`/api/competitions/calendar?season=${season}`, { credentials: 'same-origin' });
        if (!resp.ok) throw new Error(`API /api/competitions/calendar returned ${resp.status}`);
        const body = (await resp.json()) as CompetitionCalendar;
        cache.set(season, body);
        if (!cancelled) bump((n) => n + 1);
      } catch (e) {
        console.error('Error loading /api/competitions/calendar', e);
        if (!cancelled) setFailedSeason(season);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [season]);

  const data = cache.get(season) ?? null;
  const error = !data && failedSeason === season;
  return { data, loading: !data && !error, error };
}
