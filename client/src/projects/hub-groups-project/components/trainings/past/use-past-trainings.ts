import { useCallback, useEffect, useState } from 'react';
import type { PastTraining } from '../../../types';

/**
 * Прошедшие занятия с ответами или планом дорожек — список «Past» тренера (CF9,
 * docs/plans/trainings-coach-flow-plan.md): `GET /api/hub-groups/{id}/past-trainings`. Сервер
 * отдаёт их только управляющему, за `TrainingRsvpRules.PastDaysBack` дней, сначала новые.
 * Данные личные — без кэша. Грузит контекст таба (`trainings-context.tsx`), не секция.
 */
export interface PastTrainingsState {
  /** `null` — ещё не пришли, не управляющий или ошибка (список тогда просто не рисуется). */
  list: PastTraining[] | null;
  reload: () => void;
}

export function usePastTrainings(groupId: number, enabled: boolean): PastTrainingsState {
  const [list, setList] = useState<PastTraining[] | null>(null);
  const [version, setVersion] = useState(0);
  const reload = useCallback(() => setVersion((v) => v + 1), []);

  useEffect(() => {
    if (!enabled) { setList(null); return undefined; }
    let cancelled = false;
    fetch(`/api/hub-groups/${groupId}/past-trainings`, { credentials: 'include', cache: 'no-store' })
      .then((r) => (r.ok ? (r.json() as Promise<PastTraining[]>) : null))
      .then((data) => { if (!cancelled) setList(data); })
      .catch(() => { if (!cancelled) setList(null); });
    return () => { cancelled = true; };
  }, [groupId, enabled, version]);

  return { list, reload };
}
