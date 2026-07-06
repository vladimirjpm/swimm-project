import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import Helper from '../../../utils/helpers/data-helper';
import { getFilterData } from './filter-types';
import FilterCard from './filter-card';

const FilterPoolType: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filterData = getFilterData();

  if (!filterData) return null;

  const updateFilter = (pool_type: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, pool_type },
      }),
    );
  };

  const isActive = filters.pool_type !== 'all';

  return (
    <FilterCard
      title="Pool Type"
      summary={isActive ? filters.pool_type : 'All'}
      isActive={isActive}
    >
      <div className="flex flex-wrap gap-2">
        {filterData.pool_type.map((type) => (
          <button
            key={type}
            className={`fseg ${
              (type === 'all' && filters.pool_type === 'all') ||
              (type !== 'all' &&
                filters.pool_type !== 'all' &&
                Helper.resolvePoolType(filters.pool_type) ===
                  Helper.resolvePoolType(type))
                ? 'fseg-active'
                : ''
            }`}
            onClick={() => updateFilter(type)}
          >
            {type}
          </button>
        ))}
      </div>
    </FilterCard>
  );
};

export default FilterPoolType;
