import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import Helper from '../../../utils/helpers/data-helper';
import { getFilterData } from './filter-types';

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

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Pool Type</h3>
      <div className="flex flex-wrap">
        {filterData.pool_type.map((type) => (
          <button
            key={type}
            className={`px-3 py-1 m-1 border rounded transition-colors ${
              (type === 'all' && filters.pool_type === 'all') ||
              (type !== 'all' &&
                filters.pool_type !== 'all' &&
                Helper.resolvePoolType(filters.pool_type) ===
                  Helper.resolvePoolType(type))
                ? 'theme-btn-active'
                : 'theme-btn'
            }`}
            onClick={() => updateFilter(type)}
          >
            {type}
          </button>
        ))}
      </div>
    </div>
  );
};

export default FilterPoolType;
