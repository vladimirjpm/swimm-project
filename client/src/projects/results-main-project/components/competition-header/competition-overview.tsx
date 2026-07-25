import React from 'react';
import type { CompetitionOverview } from './types';
import { cardStyle } from './overview-shared';
import CompetitionSummary from './competition-summary';
import CompetitionBestSwim from './competition-best-swim';
import CompetitionMostDecorated from './competition-most-decorated';
import CompetitionNewRecords from './competition-new-records';
import CompetitionHighPointAward from './competition-high-point-award';
import { CompetitionTopClubs, CompetitionTopClubsByGender } from './competition-top-clubs';

// Контент таба Overview (design_handoff вариант 1b): grid 12 (слева span-8, справа span-4).
// ОРКЕСТРАТОР — только раскладка + резолв данных; каждый блок вынесен отдельным
// компонентом-модулем (competition-*.tsx), помечен data-module в DOM.

interface Props {
  overview: CompetitionOverview | null;
  loading: boolean;
  /** Переход в другой таб (линки «Open … tab →», клик по клубу). */
  onOpenTab(tab: 'swims' | 'clubs' | 'media' | 'records'): void;
  /** Диплинк на конкретный заплыв (best swim, строки рекордов). */
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
  /** Drill-down клуба: таб Clubs с выбранным клубом. */
  onOpenClub?(club: string): void;
}

export default function CompetitionOverviewContent({ overview, loading, onOpenTab, onOpenSwim, onOpenClub }: Props) {
  if (!overview) {
    return (
      <div className="mt-4 flex min-h-[140px] items-center justify-center rounded-[14px] text-[13px] font-semibold"
        style={{ ...cardStyle, color: 'var(--theme-mode-text-muted)' }}>
        {loading ? 'Loading overview…' : 'No overview data for this competition.'}
      </div>
    );
  }

  // Разбивка ♂/♀ (вариант 4). Фолбэк на одиночные best_swim/top_medalist — если API
  // ещё старый (нет гендерных полей) или пол не определён, чтобы карточки не пропадали.
  const bestMale = overview.best_swim_male ?? (overview.best_swim?.gender === 'male' ? overview.best_swim : null);
  const bestFemale = overview.best_swim_female ?? (overview.best_swim?.gender === 'female' ? overview.best_swim : null);
  const bestSingle = !bestMale && !bestFemale ? overview.best_swim : null;
  const medalMale = overview.top_medalist_male;
  const medalFemale = overview.top_medalist_female;
  const medalSingle = !medalMale && !medalFemale ? overview.top_medalist : null;

  const hasBestOrMedal = bestMale || bestFemale || bestSingle || medalMale || medalFemale || medalSingle;

  return (
    <div className="mt-4 grid grid-cols-1 gap-3 lg:grid-cols-12">
      {/* Левая колонка */}
      <div className="flex flex-col gap-3 lg:col-span-8">
        {/* Best swim (4a) + Most decorated (4b) — рядом. */}
        {hasBestOrMedal && (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <CompetitionBestSwim male={bestMale} female={bestFemale} single={bestSingle} />
            <CompetitionMostDecorated male={medalMale} female={medalFemale} single={medalSingle} />
          </div>
        )}

        {/* New records (половина) слева · High Point Award правее. */}
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2 lg:items-start">
          <CompetitionNewRecords records={overview.records} onOpenTab={onOpenTab} onOpenSwim={onOpenSwim} />
          <CompetitionHighPointAward awards={overview.high_point_awards} />
        </div>
      </div>

      {/* Правая колонка */}
      <div className="flex flex-col gap-3 lg:col-span-4">
        <CompetitionSummary summary={overview.summary} />
        <CompetitionTopClubs clubs={overview.top_clubs} onOpenTab={onOpenTab} onOpenClub={onOpenClub} />
        <CompetitionTopClubsByGender men={overview.top_clubs_men} women={overview.top_clubs_women} />
      </div>
    </div>
  );
}
