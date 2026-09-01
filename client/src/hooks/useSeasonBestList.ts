/**
 * Данные страницы `/season-best`: ранжированный список одной дисциплины за сезон
 * (`GET /api/season-best/list`) и опции её фильтров (`GET /api/season-best/options`).
 *
 * ⚠ Не путать с [`useSeasonBest`](./useSeasonBest.ts): тот тянет `GET /api/season-best` —
 * по одному лидеру на ступень возраста для таба рядом с возрастными рекордами. Здесь —
 * сам список ВНУТРИ одной ступени, где один пловец законно занимает несколько мест.
 *
 * Ответы кэшируются на сервере сутки и отдаются с ETag, поэтому своего кэша тут нет.
 */
import { useEffect, useState } from 'react';
import type { ShowcaseSeasonNotice } from '../utils/helpers/season-helper';

export interface SeasonBestListItem {
  place: number;
  /** Какой это по счёту заплыв ЭТОГО пловца: 1 — лучший. Отличает повтор от нового человека. */
  attempt: number;
  result_id: number;
  time: string;
  time_ms?: number | null;
  suspect_reason?: string | null;
  points: number;
  /** Отставание от лидера среза в мс; 0 у лидера. */
  gap_ms: number;
  swimmer_id: number;
  name: string;
  name_en?: string | null;
  gender: 'male' | 'female';
  age: number;
  /** Возрастная группа протокола («25-29»); есть только у мастерских заплывов. */
  age_group?: string | null;
  club_id: number;
  club?: string | null;
  /** null — у клуба нет настоящего английского имени (в базе оно копия ивритского). */
  club_en?: string | null;
  competition_id: number;
  competition?: string | null;
  pool_type?: string | null;
  date: string;
}

export interface SeasonBestClubOption {
  club_id: number;
  name: string;
  name_en?: string | null;
  swims: number;
}

export interface SeasonBestListResponse {
  season: number;
  season_label: string;
  style: string;
  distance: string;
  pool_type?: string | null;
  gender?: string | null;
  age?: number | null;
  age_to?: number | null;
  masters: boolean;
  age_group?: string | null;
  club_id?: number | null;
  best_per_swimmer: boolean;
  total: number;
  offset: number;
  limit: number;
  swimmers: number;
  meets: number;
  clubs: SeasonBestClubOption[];
  /** «Новый сезон откроется после зимнего чемпионата»; null — сезон открыт. */
  season_notice: ShowcaseSeasonNotice | null;
  data: SeasonBestListItem[];
}

export interface SeasonBestSeasonOption {
  season: number;
  label: string;
  meets: number;
  is_display_default: boolean;
}

export interface SeasonBestEventOption {
  style: string;
  distances: string[];
}

export interface SeasonBestOptions {
  seasons: SeasonBestSeasonOption[];
  /**
   * Витрина держит прошлый сезон — карусель по умолчанию стоит на нём, и страница обязана
   * объяснить почему (docs/season-boundary-rule.md). null — сезон открыт.
   */
  season_notice: ShowcaseSeasonNotice | null;
  events: SeasonBestEventOption[];
  /** Возрастные группы мастерских протоколов; пусто — мастерских стартов в базе нет. */
  age_groups: string[];
  pools: string[];
}

export interface SeasonBestListArgs {
  style: string | null;
  distance: string | null;
  poolType?: string | null;
  season?: number | null;
  age?: number | null;
  ageTo?: number | null;
  /** Мастерский срез: другие соревнования и другая ось возраста (группы). */
  masters?: boolean;
  ageGroup?: string | null;
  gender?: string | null;
  clubId?: number | null;
  bestPerSwimmer?: boolean;
  limit?: number;
}

export interface LoadState<T> {
  data: T | null;
  loading: boolean;
  error: boolean;
}

const idle = <T, >(): LoadState<T> => ({ data: null, loading: false, error: false });

/**
 * Список среза. Без стиля или дистанции запрос НЕ уходит: сервер на такой ответил бы 400,
 * а страница законно открывается и без выбранной дисциплины.
 */
export function useSeasonBestList(args: SeasonBestListArgs): LoadState<SeasonBestListResponse> {
  const {
    style, distance, poolType, season, age, ageTo, masters, ageGroup,
    gender, clubId, bestPerSwimmer, limit = 50,
  } = args;
  const [state, setState] = useState<LoadState<SeasonBestListResponse>>(idle);

  useEffect(() => {
    if (!style || !distance) { setState(idle); return; }

    const params = new URLSearchParams({ style, distance: String(distance) });
    if (poolType === '25m' || poolType === '50m') params.set('pool', poolType);
    if (season != null) params.set('season', String(season));
    if (age != null) params.set('age', String(age));
    if (ageTo != null) params.set('age_to', String(ageTo));
    // Группа едет только вместе с признаком мастерского среза — сервер иначе её игнорирует.
    if (masters) {
      params.set('masters', 'true');
      if (ageGroup) params.set('age_group', ageGroup);
    }
    if (gender) params.set('gender', gender);
    if (clubId != null) params.set('club', String(clubId));
    if (bestPerSwimmer) params.set('best', 'true');
    params.set('limit', String(limit));

    const controller = new AbortController();
    setState((prev) => ({ data: prev.data, loading: true, error: false }));

    fetch(`/api/season-best/list?${params.toString()}`, { signal: controller.signal })
      .then((res) => (res.ok ? res.json() as Promise<SeasonBestListResponse> : Promise.reject(res.status)))
      .then((json) => setState({ data: json, loading: false, error: false }))
      .catch((e) => {
        if (e?.name === 'AbortError' || controller.signal.aborted) return;
        setState({ data: null, loading: false, error: true });
      });

    return () => controller.abort();
  }, [style, distance, poolType, season, age, ageTo, masters, ageGroup,
      gender, clubId, bestPerSwimmer, limit]);

  return state;
}

/** Сезоны для карусели и стили с дистанциями для селектора дисциплины. Грузятся один раз. */
export function useSeasonBestOptions(): LoadState<SeasonBestOptions> {
  const [state, setState] = useState<LoadState<SeasonBestOptions>>(
    () => ({ data: null, loading: true, error: false }),
  );

  useEffect(() => {
    const controller = new AbortController();

    fetch('/api/season-best/options', { signal: controller.signal })
      .then((res) => (res.ok ? res.json() as Promise<SeasonBestOptions> : Promise.reject(res.status)))
      .then((json) => setState({ data: json, loading: false, error: false }))
      .catch((e) => {
        if (e?.name === 'AbortError' || controller.signal.aborted) return;
        setState({ data: null, loading: false, error: true });
      });

    return () => controller.abort();
  }, []);

  return state;
}
