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

const FilterClub: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filteredByTypeResults = useFilteredByTypeResults();

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
  }, [filteredByTypeResults]);

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
        <div className="text-[11px] text-[var(--theme-mode-text-muted)]">
          ⭐ rating · 🏊 swimmers · ✅ events
        </div>
        <button
          className={`fseg self-start ${filters.club === 'all' ? 'fseg-active' : ''}`}
          onClick={() => updateFilter('all')}
        >
          all
        </button>
        {availableClubs.map(
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
