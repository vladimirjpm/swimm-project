import { useEffect, useState } from 'react';
import { useAppSelector } from '../../../../store/store';
import type { CompetitionOverview } from './types';

// Дэшборд соревнования: GET /api/competition-overview?competitionId=|eventId=[&combined=true].
// Модульный кэш по ключу источника — переключение табов не перезапрашивает
// (серверный ETag/кэш всё равно есть, это только от лишних fetch в сессии).
const cache = new Map<string, CompetitionOverview>();

export function useCompetitionOverview(sourceParams?: Record<string, string>) {
  const [overview, setOverview] = useState<CompetitionOverview | null>(null);
  const [loading, setLoading] = useState(false);

  // Тоггл «Combine All Results» — тот же, что подменяет места в таблице результатов.
  // Overview обязан ему следовать, иначе на одном экране таблица показывает места
  // внутри заплыва, а Top clubs — общий зачёт дисциплины.
  const isCombined = useAppSelector((state) => !!state.filterSelected.is_recalculated);

  const competitionId = sourceParams?.competitionId;
  const eventId = sourceParams?.eventId;
  const source = competitionId ? `c:${competitionId}` : eventId ? `e:${eventId}` : null;
  const key = source ? `${source}:${isCombined ? 'combined' : 'plain'}` : null;

  useEffect(() => {
    if (!key) { setOverview(null); return; }
    const cached = cache.get(key);
    if (cached) { setOverview(cached); return; }

    let cancelled = false;
    setLoading(true);
    setOverview(null);
    const qs = (competitionId
      ? `competitionId=${encodeURIComponent(competitionId)}`
      : `eventId=${encodeURIComponent(eventId!)}`)
      + (isCombined ? '&combined=true' : '');
    (async () => {
      try {
        const r = await fetch(`/api/competition-overview?${qs}`);
        if (!r.ok) return;
        const dto: CompetitionOverview = await r.json();
        if (cancelled) return;
        cache.set(key, dto);
        setOverview(dto);
      } catch {
        /* нет overview — шапка живёт на title, контент таба покажет заглушку */
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  return { overview, loading };
}
