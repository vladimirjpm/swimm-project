import { useEffect, useState } from 'react';
import type { ShowcaseSeasonNotice } from '../utils/helpers/season-helper';

/**
 * Сборный ответ страницы клуба — GET /api/clubs/{id}/overview?season=&group=.
 * Одна загрузка кормит Hero, фильтры, грид, таблицу зачёта, историю и топ пловцов;
 * ростер и рекорды догружаются отдельно (у них своя пагинация и свой фильтр бассейна).
 *
 * ⚠ Сравнимая между соревнованиями величина — только РАНГ. Очки считаются по правилу
 * конкретного старта, поэтому их сумма за сезон — сумма по разным правилам; подписывай
 * её честно и не выдавай за рейтинг (docs/plans/club-page-model.md §5).
 */

export interface ClubProfile {
  id: number;
  /** Id из URL. Отличается от id, если клуб склеен с другим (мягкий merge). */
  requested_id: number;
  name: string;
  name_en: string;
  country_code: string | null;
  country_name: string | null;
  official_group_slug: string | null;
  official_group_name: string | null;
  swimmer_count: number;
  first_season: number | null;
  last_season: number | null;
}

export interface ClubKpi {
  points: number;
  gold: number;
  silver: number;
  bronze: number;
  competitions: number;
  best_rank: number | null;
  /** Плитки Hero. Скоуп у них РАЗНЫЙ и подписан в UI (см. club-hero.tsx). */
  championships: number;
  championship_wins: number;
  records: number;
  season_bests: number;
  /** Метка витринного сезона, «2025/26» (docs/season-boundary-rule.md). */
  showcase_season: string | null;
  /**
   * «Новый сезон откроется после зимнего чемпионата»; null — сезон открыт. Плитка season
   * bests подписана прошлым сезоном, и без этой оговорки цифра выглядит протухшей.
   */
  season_notice: ShowcaseSeasonNotice | null;
}

export interface ClubSeasonOption {
  season: number;
  /** «2025/26». */
  label: string;
}

export interface ClubGroupTile {
  key: string;
  name: string;
  badge: string | null;
  winter_rank: number | null;
  summer_rank: number | null;
  open_water_rank: number | null;
}

export interface ClubGridCell {
  rank: number;
  points: number;
  clubs: number;
  /** Зачётная единица — по ней открывается таблица standings. */
  competition_id: number;
}

export interface ClubGridRow {
  group_key: string;
  group_name: string;
  badge: string | null;
  winter: ClubGridCell | null;
  summer: ClubGridCell | null;
  open_water: ClubGridCell | null;
}

export interface ClubGridYear {
  season: number;
  label: string;
  rows: ClubGridRow[];
}

export interface ClubStandingRow {
  rank: number;
  club_id: number;
  club_name: string;
  club_name_en: string;
  points: number;
  gold: number;
  silver: number;
  bronze: number;
  swimmer_count: number;
  scoring_swims: number;
  is_us: boolean;
}

export interface ClubStandingsTable {
  competition_id: number;
  competition_name: string;
  date: string;
  season: number;
  /** winter | summer | openwater | null. */
  kind: string | null;
  group_key: string | null;
  group_name: string | null;
  club_count: number;
  rows: ClubStandingRow[];
}

export interface ClubTimelineItem {
  competition_id: number;
  name: string;
  date: string;
  season: number;
  kind: string | null;
  group_key: string | null;
  group_name: string | null;
  rank: number;
  points: number;
  gold: number;
  silver: number;
  bronze: number;
  swimmer_count: number;
}

export interface ClubTopSwimmer {
  swimmer_id: number;
  last_name: string;
  first_name: string;
  last_name_en: string;
  first_name_en: string;
  gender: string | null;
  age: number | null;
  points: number;
  gold: number;
  silver: number;
  bronze: number;
}

export interface ClubOverview {
  club: ClubProfile;
  kpi: ClubKpi;
  seasons: ClubSeasonOption[];
  groups: ClubGroupTile[];
  grid: ClubGridYear[];
  standings: ClubStandingsTable | null;
  timeline: ClubTimelineItem[];
  top_swimmers: ClubTopSwimmer[];
}

/** Скоуп страницы: его читают ВСЕ карточки, своих фильтров у них нет. */
export interface ClubScope {
  /** null — все сезоны. */
  season: number | null;
  /** null — все группы; иначе Category.Key. */
  group: string | null;
  /** Какой зачёт раскрыт таблицей; null — самый свежий в скоупе. */
  standingCompetitionId: number | null;
}

export interface UseClubOverviewResult {
  data: ClubOverview | null;
  loading: boolean;
  error: string | null;
}

export function useClubOverview(clubId: number | null, scope: ClubScope): UseClubOverviewResult {
  const [data, setData] = useState<ClubOverview | null>(null);
  const [loading, setLoading] = useState<boolean>(clubId != null);
  const [error, setError] = useState<string | null>(null);

  const { season, group, standingCompetitionId } = scope;

  useEffect(() => {
    if (clubId == null) {
      setData(null);
      setLoading(false);
      return;
    }

    const qs = new URLSearchParams();
    if (season != null) qs.set('season', String(season));
    if (group != null) qs.set('group', group);
    if (standingCompetitionId != null) qs.set('standing', String(standingCompetitionId));
    const url = `/api/clubs/${clubId}/overview${qs.toString() ? `?${qs}` : ''}`;

    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(url)
      .then((res) => {
        if (res.status === 404) throw new Error('not-found');
        if (!res.ok) throw new Error(`http-${res.status}`);
        return res.json();
      })
      .then((json: ClubOverview) => {
        if (!cancelled) setData(json);
      })
      .catch((e: Error) => {
        if (!cancelled) {
          setData(null);
          setError(e.message);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [clubId, season, group, standingCompetitionId]);

  return { data, loading, error };
}
