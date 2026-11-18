import { useEffect, useState } from 'react';
import type { LanePlanEvent } from '../../../types';
import { lanePlansApi } from '../../lane-plans-api';

/**
 * Лента «Since publishing» плана занятия (CF8 потока тренера): грузит её, пока план опубликован, и
 * перечитывает по `version` — любой смене, после которой в ленте могла появиться строка (план
 * сохранён / опубликован — новый список планов контекста; ответы занятия — новый `rsvp`).
 * Черновик или нет плана — `null`, запроса нет. Ошибка — `null`: лента подсказка, а не обязанность.
 */
export function usePlanEvents(groupId: number, sessionId: string | null, published: boolean, version: unknown): LanePlanEvent[] | null {
  const [events, setEvents] = useState<{ key: string; list: LanePlanEvent[] } | null>(null);

  useEffect(() => {
    if (!sessionId || !published) return undefined;
    let cancelled = false;
    lanePlansApi.events(groupId, sessionId).then((r) => {
      if (!cancelled && r.ok && r.data) setEvents({ key: sessionId, list: r.data });
    });
    return () => { cancelled = true; };
  }, [groupId, sessionId, published, version]);

  // Ответ другого занятия (переход ‹ ›) и снятая публикация не показываются.
  return published && events?.key === sessionId ? events.list : null;
}
