import { useEffect, useState } from 'react';

/**
 * Данные табов страницы спортсмена (docs/plans/athlete-page-plan.md §3).
 * По хуку на таб — грузим лениво, когда таб открыли: страница целиком это пять запросов,
 * а открывают обычно один-два.
 *
 * Сервер отдаёт всё уже посчитанным (дельты, личники, медали): на клиенте арифметики
 * сезона нет и быть не должно — она живёт в SeasonAggregator/SwimmerPageBuilder.
 */

/** Признак качества времени — то же, что понимает UI_SwimTime. */
export interface SwimQualityDto {
  kind: 'protocol' | 'record';
  reason?: string | null;
}

export interface CompetitionRef {
  id: number;
  eventId?: number | null;
  name: string;
  isChampionship: boolean;
}

export interface MedalCounts {
  gold: number;
  silver: number;
  bronze: number;
}

export interface SwimmerCompetition {
  competitionId: number;
  eventId?: number | null;
  name: string;
  date: string;
  isChampionship: boolean;
  /** winter | summer | openwater | null — роль старта в сезоне. */
  kind?: string | null;
  poolType: string;
  waterKind: string;
  swims: number;
  points: number;
  medals: MedalCounts;
  bestPlace?: number | null;
}

export interface SwimmerSummary {
  season: number | null;
  label: string;
  points: number;
  medals: MedalCounts;
  swims: number;
  events: number;
  competitionCount: number;
  personalBests: number;
  competitions: SwimmerCompetition[];
}

export interface SwimmerBestTime {
  disciplineKey: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  poolType: string;
  waterKind: string;
  time?: string | null;
  timeMs?: number | null;
  quality?: SwimQualityDto | null;
  points?: number | null;
  place?: number | null;
  /** 'prelim' | 'final' | null — место prelim-заплыва рисуется без медали. */
  heatType?: string | null;
  ageInSeason?: number | null;
  splits?: string | null;
  date: string;
  competition: CompetitionRef;
  resultId: number;
  isCareerBest: boolean;
}

export interface SwimmerPersonalBest {
  disciplineKey: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  poolType: string;
  time?: string | null;
  timeMs?: number | null;
  quality?: SwimQualityDto | null;
  points?: number | null;
  date: string;
  competition: CompetitionRef;
  resultId: number;
  holdsClubBest: boolean;
  deltaToClubBestMs?: number | null;
  holdsNationalAgeRecord: boolean;
  deltaToNationalAgeRecordMs?: number | null;
  nationalAgeRecordTime?: string | null;
  nationalAgeRecordQuality?: SwimQualityDto | null;
  nationalAgeKey?: string | null;
}

export interface SwimmerProgressPoint {
  date: string;
  time?: string | null;
  timeMs?: number | null;
  isPb: boolean;
  quality?: SwimQualityDto | null;
  points?: number | null;
  place?: number | null;
  /** 'prelim' | 'final' | null — место prelim-заплыва рисуется без медали. */
  heatType?: string | null;
  ageInSeason?: number | null;
  season: number;
  competition: CompetitionRef;
  resultId: number;
}

export interface SwimmerProgress {
  disciplineKey: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  poolType: string;
  points: SwimmerProgressPoint[];
}

/**
 * Значение `?season=` для запроса: null (режим All) → «all», иначе год начала сезона.
 * Отдельная функция, потому что «сезон не выбран» и «сезон ещё не известен» — разные
 * состояния, и путать их значит грузить карьеру вместо сезона на первом кадре.
 */
const seasonParam = (season: number | null) => (season == null ? 'all' : String(season));

interface Loaded<T> {
  data: T | null;
  loading: boolean;
  error: boolean;
}

/** Общая загрузка JSON с отменой по смене входа. `null` в url — запрос не нужен. */
function useJson<T>(url: string | null): Loaded<T> {
  const [state, setState] = useState<Loaded<T>>({ data: null, loading: url != null, error: false });

  useEffect(() => {
    if (url == null) {
      setState({ data: null, loading: false, error: false });
      return;
    }
    let alive = true;
    // Данные НЕ обнуляем: при смене сезона старая панель остаётся на экране и заменяется
    // на месте — иначе страница прыгала бы на каждый клик по карусели (урок клуба).
    setState((s) => ({ ...s, loading: true, error: false }));
    (async () => {
      try {
        const r = await fetch(url);
        if (!alive) return;
        if (!r.ok) { setState({ data: null, loading: false, error: true }); return; }
        const data: T = await r.json();
        if (alive) setState({ data, loading: false, error: false });
      } catch {
        if (alive) setState({ data: null, loading: false, error: true });
      }
    })();
    return () => { alive = false; };
  }, [url]);

  return state;
}

export const useSwimmerSummary = (id: number | null, season: number | null, enabled = true) =>
  useJson<SwimmerSummary>(
    id != null && enabled ? `/api/swimmers/${id}/summary?season=${seasonParam(season)}` : null);

export const useSwimmerBestTimes = (id: number | null, season: number | null, enabled = true) =>
  useJson<SwimmerBestTime[]>(
    id != null && enabled ? `/api/swimmers/${id}/best-times?season=${seasonParam(season)}` : null);

export const useSwimmerPersonalBests = (id: number | null, poolType: string, enabled = true) =>
  useJson<SwimmerPersonalBest[]>(
    id != null && enabled ? `/api/swimmers/${id}/personal-bests?poolType=${poolType}` : null);

export const useSwimmerProgress = (id: number | null, disciplineKey: string | null) =>
  useJson<SwimmerProgress>(
    id != null && disciplineKey
      ? `/api/swimmers/${id}/progress?disciplineKey=${encodeURIComponent(disciplineKey)}`
      : null);
