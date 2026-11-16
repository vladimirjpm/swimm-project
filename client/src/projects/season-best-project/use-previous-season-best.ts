import { useMemo } from 'react';
import { useSeasonBestList, type SeasonBestListItem } from '../../hooks/useSeasonBestList';
import { seasonLabel } from '../../utils/helpers/season-helper';
import type { SbFilters } from './sb-filters-model';
import { isSameSlice, previousBestLines, type SbPreviousLine, type SbSliceWanted } from './sb-previous-best-model';

export interface PreviousSeasonBest {
  /** «2025/26». */
  seasonLabel: string;
  lines: SbPreviousLine<SeasonBestListItem>[];
}

/**
 * Данные карточки «лучший прошлого сезона в этой категории» (sb-previous-best.tsx).
 *
 * Два запроса `/api/season-best/list` с `best=true` и БЕЗ клуба — нынешний сезон и прошлый,
 * остальной срез как у страницы. Нынешний нужен отдельно от основного списка: тот может
 * быть отфильтрован клубом, а лидер категории — общий на страну. Оба ответа лежат в
 * серверном кэше (ключ содержит сезон), сутки + ETag.
 *
 * Возраст в запросе прошлого сезона тот же число — сервер считает его от СВОЕГО сезона,
 * поэтому «10 лет» там — десятилетки прошлого сезона. Это и нужно.
 */
export function usePreviousSeasonBest(filters: SbFilters, enabled: boolean): PreviousSeasonBest | null {
  const on = enabled && filters.season != null && !!filters.stroke && !!filters.distance;
  const masters = filters.ageGroup != null;
  const base = {
    // Без стиля хук запрос не шлёт — так карточка и выключается.
    style: on ? filters.stroke : null,
    distance: filters.distance,
    poolType: filters.poolType,
    age: filters.age,
    ageTo: filters.ageTo,
    masters,
    ageGroup: filters.ageGroup,
    gender: filters.gender,
    bestPerSwimmer: true,
    limit: 50,
  };
  const current = useSeasonBestList({ ...base, season: filters.season });
  const previous = useSeasonBestList({ ...base, season: filters.season != null ? filters.season - 1 : null });

  return useMemo(() => {
    if (!on || filters.season == null || !previous.data) return null;

    const wanted = (season: number): SbSliceWanted => ({
      season,
      style: filters.stroke ?? '',
      distance: filters.distance ?? '',
      poolType: filters.poolType,
      gender: filters.gender,
      // Сервер эхом возвращает возраст только для обычного среза, группу — только для мастерского.
      age: masters ? null : filters.age,
      ageTo: masters ? null : filters.ageTo,
      ageGroup: masters ? filters.ageGroup : null,
    });

    const prevSeason = filters.season - 1;
    if (!isSameSlice(previous.data, wanted(prevSeason))) return null;
    const currentRows = current.data && isSameSlice(current.data, wanted(filters.season))
      ? current.data.data
      : [];

    return {
      seasonLabel: seasonLabel(prevSeason),
      lines: previousBestLines(currentRows, previous.data.data, filters.gender),
    };
  }, [on, masters, filters, current.data, previous.data]);
}
