import { useEffect, useState } from 'react';

export interface ClubSummary {
  club: string;
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
 */
export function useClubSummary(
  sourceParams: Record<string, string> | undefined,
  enabled = true,
): ClubSummary[] {
  const [clubs, setClubs] = useState<ClubSummary[]>([]);
  const key = sourceParams ? JSON.stringify(sourceParams) : '';

  useEffect(() => {
    if (!enabled || !sourceParams) {
      setClubs([]);
      return;
    }
    let alive = true;
    const qs = new URLSearchParams(sourceParams);
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
    // key сериализует sourceParams — рефетч только при смене источника.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, enabled]);

  return clubs;
}
