import { useCallback, useEffect, useState } from 'react';
import type { HubGroupLevels } from '../types';
import { lanePlansApi } from './lane-plans-api';

/**
 * Уровни группы + уровень пловца (docs/plans/lane-plans-plan.md) — общий для карточки
 * `Levels` в табе Admin и выпадашки уровня в табе Swimmers. Ручки только управляющим
 * (`/api/me/hub-groups/{id}/levels`), поэтому `enabled = false` не делает ни одного запроса:
 * участнику и гостю уровни не видны вовсе.
 *
 * Уровень пловца ставится сразу по выбору, оптимистично: не сохранилось — откат с текстом
 * ошибки сервера.
 */
export function useGroupLevels(groupId: number, enabled = true) {
  const [data, setData] = useState<HubGroupLevels | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [pendingSwimmer, setPendingSwimmer] = useState<number | null>(null);
  const [pendingAccount, setPendingAccount] = useState<number | null>(null);

  useEffect(() => {
    if (!enabled) return undefined;
    let cancelled = false;
    setLoadError(false);
    lanePlansApi.levels(groupId).then((r) => {
      if (cancelled) return;
      if (r.ok && r.data) setData(r.data);
      else setLoadError(true);
    });
    return () => { cancelled = true; };
  }, [groupId, enabled]);

  /** Ставит уровень (null — «без уровня»). Возвращает текст ошибки или null. */
  const setSwimmerLevel = useCallback(async (swimmerId: number, levelId: number | null): Promise<string | null> => {
    const previous = data?.swimmers.find((s) => s.swimmerId === swimmerId)?.levelId ?? null;
    setData((d) => d && applySwimmerLevel(d, swimmerId, levelId));
    setPendingSwimmer(swimmerId);
    const result = await lanePlansApi.setSwimmerLevel(groupId, swimmerId, levelId);
    setPendingSwimmer(null);
    if (result.ok) return null;
    setData((d) => d && applySwimmerLevel(d, swimmerId, previous));
    return result.error ?? 'Could not save the level. Try again.';
  }, [data, groupId]);

  /** Уровень аккаунта без пловца (Ш3.1); null — «без уровня». Возвращает текст ошибки или null. */
  const setAccountLevel = useCallback(async (userId: number, levelId: number | null): Promise<string | null> => {
    const previous = data?.accounts?.find((a) => a.userId === userId)?.levelId ?? null;
    setData((d) => d && applyAccountLevel(d, userId, levelId));
    setPendingAccount(userId);
    const result = await lanePlansApi.setAccountLevel(groupId, userId, levelId);
    setPendingAccount(null);
    if (result.ok) return null;
    setData((d) => d && applyAccountLevel(d, userId, previous));
    return result.error ?? 'Could not save the level. Try again.';
  }, [data, groupId]);

  return { data, setData, loadError, pendingSwimmer, setSwimmerLevel, pendingAccount, setAccountLevel };
}

/** Уровень пловца + пересчёт счётчиков — локально, до ответа сервера. */
export function applySwimmerLevel(data: HubGroupLevels, swimmerId: number, levelId: number | null): HubGroupLevels {
  const swimmers = data.swimmers.map((s) => (s.swimmerId === swimmerId ? { ...s, levelId } : s));
  const counts = new Map<number, number>();
  swimmers.forEach((s) => { if (s.levelId != null) counts.set(s.levelId, (counts.get(s.levelId) ?? 0) + 1); });
  return {
    ...data,
    levels: data.levels.map((l) => ({ ...l, swimmerCount: counts.get(l.id) ?? 0 })),
    swimmers,
  };
}

/** Уровень аккаунта + пересчёт счётчиков аккаунтов — локально, до ответа сервера. */
export function applyAccountLevel(data: HubGroupLevels, userId: number, levelId: number | null): HubGroupLevels {
  const accounts = (data.accounts ?? []).map((a) => (a.userId === userId ? { ...a, levelId } : a));
  const counts = new Map<number, number>();
  accounts.forEach((a) => { if (a.levelId != null) counts.set(a.levelId, (counts.get(a.levelId) ?? 0) + 1); });
  return {
    ...data,
    levels: data.levels.map((l) => ({ ...l, accountCount: counts.get(l.id) ?? 0 })),
    accounts,
  };
}
