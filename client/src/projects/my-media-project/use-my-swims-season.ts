import { useMemo, useState } from 'react';
import { useMySwims, seasonLabel } from './use-my-swims';
import { seasonStartYear } from '../../utils/helpers/season-helper';

/**
 * Сезон страницы и агрегат заплывов за него (`/api/me/swims`): выбор сезона, список для
 * карусели и полосы, сезон «по факту», когда выбор отдан серверу.
 */
export function useMySwimsSeason() {
  // null → сервер выберет витринный сезон; 'all' → все сезоны сразу.
  const [season, setSeason] = useState<number | 'all' | null>(null);
  const { data, loading, reload } = useMySwims(season);

  const effectiveSeason = data.season || new Date().getFullYear();
  // Выбранный сезон обязан быть в списке: иначе <select> показывает чужую подпись — так
  // страница и «залипала» на пустом 2026/27, когда данные есть только за 2025/26.
  const seasonChoices = useMemo(() => {
    const years = new Set<number>(data.seasons);
    years.add(effectiveSeason);
    // Новый (календарный) сезон в списке есть всегда, даже пока стартов в нём нет — по
    // общему правилу витрины: «новый сезон в селекторе есть, но по умолчанию не выбран»
    // (docs/season-boundary-rule.md). Выбирает же дефолт сервер — витринным сезоном.
    years.add(seasonStartYear());
    return Array.from(years).sort((a, b) => b - a);
  }, [data.seasons, effectiveSeason]);
  // Карусель говорит числом-годом или null (∞ «все сезоны»). Состояние страницы шире:
  // null в нём означает «сезон выбирает сервер» (витринный), поэтому наружу отдаём уже
  // посчитанный `effectiveSeason`, а не сырой null — иначе карусель встала бы на ∞.
  const carouselSeason = season === 'all' || data.all_seasons ? null : season ?? effectiveSeason;
  const pickSeason = (v: number | null) => setSeason(v === null ? 'all' : v);

  const seasonOptions = useMemo(
    () => seasonChoices.map((y) => ({ season: y, label: seasonLabel(y) })),
    [seasonChoices],
  );

  return {
    setSeason, data, loading, reload,
    effectiveSeason, seasonChoices, carouselSeason, pickSeason, seasonOptions,
  };
}
