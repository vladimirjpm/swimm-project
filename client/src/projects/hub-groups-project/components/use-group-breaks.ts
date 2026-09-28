import { useCallback, useEffect, useState } from 'react';
import type { HubGroupBreaks } from '../types';
import { lanePlansApi } from './lane-plans-api';

/**
 * Флаг «On break» группы (docs/plans/entity-hero-roles-plan.md §5, Ш3.1): свой перерыв
 * участника и — управляющему — все действующие перерывы и недавние возвращения.
 *
 * Данные личные: запрос только участнику и управляющему (`enabled`). Запись не оптимистичная —
 * ответ сервера приходит целиком и заменяет состояние; `onChanged` даёт вызывающему перечитать
 * то, что от перерыва зависит (ответы на занятие: знаменатель, вид по дорожкам).
 */
export function useGroupBreaks(groupId: number, enabled: boolean, onChanged?: () => void) {
  const [data, setData] = useState<HubGroupBreaks | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!enabled) { setData(null); return undefined; }
    let cancelled = false;
    lanePlansApi.breaks(groupId).then((r) => {
      if (!cancelled && r.ok && r.data) setData(r.data);
    });
    return () => { cancelled = true; };
  }, [groupId, enabled]);

  /** Возвращает текст ошибки сервера или null. */
  const setBreak = useCallback(async (
    input: { on_break: boolean; until?: string | null; user_id?: number | null; swimmer_id?: number | null },
  ): Promise<string | null> => {
    setBusy(true);
    const r = await lanePlansApi.setBreak(groupId, input);
    setBusy(false);
    if (r.ok && r.data) {
      setData(r.data);
      onChanged?.();
      return null;
    }
    return r.error ?? 'Could not save. Try again.';
  }, [groupId, onChanged]);

  return { data, busy, setBreak };
}
