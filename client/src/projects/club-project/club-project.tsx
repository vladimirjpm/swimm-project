import React, { useMemo, useState } from 'react';
import '../../index.css';
import './club-theme.css';
import { useMode } from '../../hooks/useMode';
import { useClubOverview, type ClubScope } from '../../hooks/useClubOverview';
import { parseRoute } from '../../utils/routes';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import ClubHero from './components/club-hero';
import ClubFilters from './components/club-filters';
import ClubGrid from './components/club-grid';

/**
 * Страница клуба (Фаза 10, план docs/plans/club-page-plan.md, этап K5).
 *
 * Устройство: Hero → ОДИН глобальный блок фильтров (сезон + зачётная группа) → карточки.
 * Каждая карточка читает выбранный скоуп и своих фильтров не заводит — единственное
 * исключение будет у стены рекордов (переключатель бассейна: 25м и 50м несравнимы).
 *
 * Тема — токены `--deep-*` из дизайн-хендоффа (club-theme.css): класс .theme-deep или
 * .theme-deep-light навешивается по глобальному режиму light/dark.
 */
function ClubProject() {
  const clubId = useMemo<number | null>(() => parseRoute().clubId, []);
  const { mode } = useMode();

  const [scope, setScope] = useState<ClubScope>({
    season: null,
    group: null,
    standingCompetitionId: null,
  });

  const { data, loading, error } = useClubOverview(clubId, scope);

  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';
  const scopeLabel =
    scope.season == null
      ? 'all seasons'
      : data?.seasons.find((s) => s.season === scope.season)?.label ?? String(scope.season);

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar />

      <main className="mx-auto max-w-[1180px] px-4 py-6" style={{ color: 'var(--deep-text)' }}>
        <div className="mb-4 flex justify-end">
          <UI_ModeToggle />
        </div>

        {clubId == null && <Notice>Club not found</Notice>}
        {clubId != null && loading && <Notice>Loading…</Notice>}
        {clubId != null && !loading && error === 'not-found' && <Notice>Club not found</Notice>}
        {clubId != null && !loading && error && error !== 'not-found' && (
          <Notice>Could not load this club</Notice>
        )}

        {data && (
          <>
            <ClubHero club={data.club} kpi={data.kpi} scopeLabel={scopeLabel} />

            <ClubFilters
              seasons={data.seasons}
              groups={data.groups}
              season={scope.season}
              group={scope.group}
              // Смена скоупа сбрасывает раскрытый зачёт: он мог принадлежать другому сезону.
              onSeason={(season) => setScope((s) => ({ ...s, season, standingCompetitionId: null }))}
              onGroup={(group) => setScope((s) => ({ ...s, group, standingCompetitionId: null }))}
            />

            <ClubGrid
              grid={data.grid}
              currentSeason={scope.season}
              selectedCompetitionId={data.standings?.competition_id ?? null}
              onPickStanding={(competitionId) =>
                setScope((s) => ({ ...s, standingCompetitionId: competitionId }))
              }
            />

            {/* Остальные карточки (Standings, Timeline, Top swimmers, Swimmers, Record wall,
                Coaches) добавляются сюда же — страница это массив независимых блоков. */}
          </>
        )}
      </main>
    </div>
  );
}

function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div
      className="deep-card text-center text-[14px] font-extrabold"
      style={{ color: 'var(--deep-text-mute)' }}
    >
      {children}
    </div>
  );
}

export default ClubProject;
