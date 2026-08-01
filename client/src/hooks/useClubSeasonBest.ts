import { useEffect, useState } from 'react';

/**
 * Season best — GET /api/clubs/{id}/season-best?pool=&season=.
 *
 * Заплывы пловцов клуба, которые в этом сезоне ЛУЧШИЕ ПО СТРАНЕ в своём слоте
 * (стиль × дистанция × бассейн × пол × возрастная ступень). Лидер считается по всей базе,
 * а не внутри клуба: карточка отвечает «наши первые в Израиле», а не «наше лучшее».
 *
 * ⚠ Две вещи, которые нельзя потерять при правках:
 *  • карточка НЕ слушает глобальный фильтр сезона страницы и не умеет «за всё время»
 *    (решение Влада 2026-08-01);
 *  • «первый в Израиле» = первый среди ИМПОРТИРОВАННОГО. Поэтому в ответе есть `meets`
 *    (сколько соревнований вошло в расчёт) и UI обязан это показывать — юниорских и
 *    взрослых чемпионатов в базе может не быть вовсе.
 *
 * Официальные рекорды — соседний хук `useClubRecordWall`.
 */

export interface ClubSeasonBestItem {
  style_name: string;
  distance: string;
  pool_type: string | null;
  gender: string;
  time_original: string;
  time_ms: number | null;
  swimmer_id: number;
  swimmer_name: string;
  swimmer_name_en: string;
  competition_name: string;
  /** dd/MM/yyyy. */
  date: string;
  points: number;
  /** «8».. «18» / «adults» / «25-29»… / «n/a». */
  age_key: string;
  /** Готовая подпись: «age 10», «adults», «masters 45-49». */
  age_label: string;
  age_order: number;
}

export interface ClubSeasonBestGroup {
  style_name: string;
  distance: string;
  pool_type: string | null;
  gender: string;
  items: ClubSeasonBestItem[];
}

interface ClubSeasonBestResponse {
  season: number;
  season_label: string;
  total: number;
  /** Сколько соревнований сезона вошло в расчёт лидерства. */
  meets: number;
  data: ClubSeasonBestGroup[];
}

export interface UseClubSeasonBestResult {
  groups: ClubSeasonBestGroup[];
  /** Метка сезона с сервера («2025/26») — идёт в заголовок карточки. */
  seasonLabel: string;
  total: number;
  /** Сколько стартов вошло в расчёт — обязательная подпись под заголовком. */
  meets: number;
  loading: boolean;
  error: string | null;
}

export function useClubSeasonBest(
  clubId: number | null,
  pool: '25m' | '50m' | null,
): UseClubSeasonBestResult {
  const [groups, setGroups] = useState<ClubSeasonBestGroup[]>([]);
  const [seasonLabel, setSeasonLabel] = useState('');
  const [total, setTotal] = useState(0);
  const [meets, setMeets] = useState(0);
  const [loading, setLoading] = useState<boolean>(clubId != null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (clubId == null) {
      setGroups([]);
      setTotal(0);
      setLoading(false);
      return;
    }

    const qs = pool ? `?pool=${pool}` : '';
    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`/api/clubs/${clubId}/season-best${qs}`)
      .then((res) => {
        if (!res.ok) throw new Error(`http-${res.status}`);
        return res.json() as Promise<ClubSeasonBestResponse>;
      })
      .then((json) => {
        if (!cancelled) {
          setGroups(json.data);
          setSeasonLabel(json.season_label);
          setTotal(json.total);
          setMeets(json.meets);
        }
      })
      .catch((e: Error) => {
        if (!cancelled) {
          setGroups([]);
          setTotal(0);
          setError(e.message);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [clubId, pool]);

  return { groups, seasonLabel, total, meets, loading, error };
}
