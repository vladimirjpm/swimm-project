import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { getFilterData } from './filter-types';
import FilterCard from './filter-card';

const FilterGender: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filterData = getFilterData();

  if (!filterData) return null;

  const updateFilter = (gender: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, gender },
      }),
    );
  };

  const genderLabel = (gen: string) =>
    gen === 'male' ? 'M' : gen === 'female' ? 'W' : 'All';

  return (
    <FilterCard
      title="Gender"
      summary={genderLabel(filters.gender)}
      isActive={filters.gender !== 'all'}
    >
      <div className="flex flex-wrap gap-2">
        {filterData.gender.map((gen) => (
          <button
            key={gen}
            className={`fseg ${filters.gender === gen ? 'fseg-active' : ''}`}
            onClick={() => updateFilter(gen)}
          >
            {genderLabel(gen)}
          </button>
        ))}
      </div>
    </FilterCard>
  );
};

export default FilterGender;
