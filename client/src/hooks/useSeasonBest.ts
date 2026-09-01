/**
 * useSeasonBest — национальный season best одной дисциплины: GET /api/season-best.
 *
 * Источник таба «Season best» рядом с возрастными рекордами (design_handoff_age_records_sb).
 * Ось возраста тут СЕЗОННАЯ (сервер считает по SeasonMath.AgeInSeason), в отличие от
 * справочника рекордов с календарной осью — расхождение осознанное, решение Влада 2026-08-22.
 *
 * Ответ кэшируется на сервере сутки и отдаётся с ETag, поэтому повторные заходы почти
 * бесплатны — отдельного клиентского кэша здесь нет.
 */
import { useEffect, useState } from 'react';
import type { ShowcaseSeasonNotice } from '../utils/helpers/season-helper';

export interface SeasonBestApiItem {
  gender: 'male' | 'female';
  age: number;
  time: string;
  time_ms?: number | null;
  swimmer_id: number;
  name: string;
  name_en?: string | null;
  club?: string | null;
  pool_type?: string | null;
  competition?: string | null;
  date: string;
  points?: number | null;
}

export interface SeasonBestApiResponse {
  season: number;
  season_label: string;
  style: string;
  distance: string;
  pool_type?: string | null;
  meets: number;
  /** «Новый сезон откроется после зимнего чемпионата»; null — сезон открыт. */
  season_notice: ShowcaseSeasonNotice | null;
  data: SeasonBestApiItem[];
}

export interface UseSeasonBestArgs {
  styleName: string;
  styleLen: string | number;
  /** '25m' | '50m' | 'all' — 'all' значит «оба бассейна в одной выборке». */
  poolType: string;
  /** Год НАЧАЛА сезона; не задан — текущий сезон на сервере. */
  season?: number | null;
  enabled?: boolean;
}

export function useSeasonBest({
  styleName, styleLen, poolType, season, enabled = true,
}: UseSeasonBestArgs): SeasonBestApiResponse | null {
  const [data, setData] = useState<SeasonBestApiResponse | null>(null);

  useEffect(() => {
    if (!enabled || !styleName || !styleLen) { setData(null); return; }

    const params = new URLSearchParams({ style: styleName, distance: String(styleLen) });
    if (poolType === '25m' || poolType === '50m') params.set('pool', poolType);
    if (season) params.set('season', String(season));

    let alive = true;
    fetch(`/api/season-best?${params.toString()}`)
      .then((res) => (res.ok ? res.json() as Promise<SeasonBestApiResponse> : null))
      .then((json) => { if (alive) setData(json); })
      .catch(() => { if (alive) setData(null); });

    return () => { alive = false; };
  }, [styleName, styleLen, poolType, season, enabled]);

  return data;
}

export default useSeasonBest;
