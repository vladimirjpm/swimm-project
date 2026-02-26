import React, { useState, useEffect } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import Helper from '../../../utils/helpers/data-helper';
import UI_ClubDetails from '../mix/club-details/club-details';
import { useFilteredByTypeResults } from './use-filtered-results';

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

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Club</h3>
      <div className="flex flex-col">
        <div className='flex flex-between'>
          <button
            className={`px-3 py-1 m-1 border rounded transition-colors ${
              filters.club === 'all' ? 'theme-btn-active' : 'theme-btn'
            }`}
            onClick={() => updateFilter('all')}
          >
            all
          </button>
          <div className='pl-4 text-sm'>⭐- rating, 🏊‍♂️ swimmers, <br /> ✅ events count</div>
        </div>
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
    </div>
  );
};

export default FilterClub;
