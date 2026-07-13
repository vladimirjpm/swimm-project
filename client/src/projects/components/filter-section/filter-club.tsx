import React, { useState, useEffect } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import Helper from '../../../utils/helpers/data-helper';
import UI_ClubDetails from '../mix/club-details/club-details';
import { useFilteredByTypeResults } from './use-filtered-results';
import FilterCard from './filter-card';
import { useResultsLoadMode } from '../../../hooks/useResultsLoadMode';
import { useClubSummary } from '../../../hooks/useClubSummary';

const FilterClub: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const sourceParams = useAppSelector((state) => state.dataSourceSelected?.sourceParams);
  const filteredByTypeResults = useFilteredByTypeResults();
  const mode = useResultsLoadMode();

  const [availableClubs, setAvailableClubs] = useState<
    Array<{
      club: string;
      points: number;
      swimmerCount: number;
      successfulCount: number;
      gold: number;
      silver: number;
      bronze: number;
    }>
  >([]);

  useEffect(() => {
    if (mode === 'paged') return;
    let cancelled = false;

    const loadClubs = async () => {
      const clubs = await Helper.getClubsSummary(filteredByTypeResults);
      if (!cancelled) {
        setAvailableClubs(clubs);
      }
    };

    loadClubs();

    return () => {
      cancelled = true;
    };
  }, [mode, filteredByTypeResults]);

  // Paged (фаза 3.4): сводку по клубам (очки/медали/пловцы) считает сервер — у клиента нет
  // полного датасета соревнования. Область — sourceParams выбранного источника. Ввод фильтрует
  // готовый (небольшой) список локально, без похода в API на каждый символ.
  const [clubQuery, setClubQuery] = useState('');
  const pagedClubs = useClubSummary(sourceParams, mode === 'paged');
  const filteredPagedClubs = clubQuery.trim()
    ? pagedClubs.filter((c) => c.club.toLowerCase().includes(clubQuery.trim().toLowerCase()))
    : pagedClubs;

  const updateFilter = (club: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, club },
      }),
    );
  };

  const isActive = filters.club !== 'all';

  return (
    <FilterCard
      title="Club"
      summary={isActive ? filters.club : 'All'}
      isActive={isActive}
    >
      <div className="flex flex-col gap-2">
        <button
          className={`fseg self-start ${filters.club === 'all' ? 'fseg-active' : ''}`}
          onClick={() => updateFilter('all')}
        >
          all
        </button>
        <div className="text-[11px] text-[var(--theme-mode-text-muted)]">
          ⭐ rating · 🏊 swimmers · ✅ events
        </div>
        {mode === 'paged' && (
          <input
            type="text"
            value={clubQuery}
            onChange={(e) => setClubQuery(e.target.value)}
            placeholder="Search club…"
            className="rounded-lg px-2.5 py-1.5 text-[13px] outline-none"
            style={{
              background: 'var(--theme-mode-input-bg)',
              border: '1px solid var(--theme-mode-border-input)',
              color: 'var(--theme-mode-text)',
            }}
          />
        )}
        {(mode === 'paged' ? filteredPagedClubs : availableClubs).map(
          ({
            club,
            points,
            swimmerCount,
            successfulCount,
            gold,
            silver,
            bronze,
          }) => (
            <UI_ClubDetails
              key={club}
              club={club}
              isSelected={filters.club === club}
              onSelect={(c) => updateFilter(c)}
              gold={gold}
              silver={silver}
              bronze={bronze}
              swimmerCount={swimmerCount}
              successfulCount={successfulCount}
              points={points}
            />
          ),
        )}
      </div>
    </FilterCard>
  );
};

export default FilterClub;
