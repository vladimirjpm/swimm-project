import { useEffect, useState } from 'react';
import { useAppSelector } from '../store/store';

export interface ClubSummary {
  club: string;
  /** Id клуба — адрес его страницы (/clubs/{id}); по имени она не открывается. */
  clubId: number;
  points: number;
  swimmerCount: number;
  successfulCount: number;
  gold: number;
  silver: number;
  bronze: number;
}

/**
 * GET /api/club-summary — сводка по клубам (очки/медали/пловцы) для источника в paged-режиме
 * (фаза 3.4). На клиенте в этом режиме нет полного датасета соревнования, поэтому агрегат
 * считает сервер. Область берётся из sourceParams выбранного источника (competitionId/eventId).
 * enabled=false (full-режим) — фетч не выполняется, там сводка считается на клиенте.
 *
 * Тоггл «Combine All Results» передаётся на сервер (&combined=true) — ровно как в
 * useCompetitionOverview: без этого таб Clubs показывал протокольный зачёт, пока Overview
 * на том же экране считал объединённый (1673 против 860 у одного клуба).
 */
export function useClubSummary(
  sourceParams: Record<string, string> | undefined,
  enabled = true,
): ClubSummary[] {
  const [clubs, setClubs] = useState<ClubSummary[]>([]);
  const isCombined = useAppSelector((state) => !!state.filterSelected.is_recalculated);
  const key = sourceParams ? JSON.stringify(sourceParams) : '';

  useEffect(() => {
    if (!enabled || !sourceParams) {
      setClubs([]);
      return;
    }
    let alive = true;
    const qs = new URLSearchParams(sourceParams);
    if (isCombined) qs.set('combined', 'true');
    fetch(`/api/club-summary?${qs}`, { credentials: 'same-origin' })
      .then((r) => (r.ok ? (r.json() as Promise<ClubSummary[]>) : []))
      .then((data) => {
        if (alive) setClubs(data);
      })
      .catch(() => {
        if (alive) setClubs([]);
      });

    return () => {
      alive = false;
    };
    // key сериализует sourceParams — рефетч при смене источника или режима зачёта.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, enabled, isCombined]);

  return clubs;
}
