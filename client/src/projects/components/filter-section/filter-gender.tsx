import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { getFilterData } from './filter-types';

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

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Gender</h3>
      <div className="flex flex-wrap">
        {filterData.gender.map((gen) => (
          <button
            key={gen}
            className={`px-3 py-1 m-1 border rounded transition-colors ${
              filters.gender === gen ? 'theme-btn-active' : 'theme-btn'
            }`}
            onClick={() => updateFilter(gen)}
          >
            {gen === 'male' ? 'M' : gen === 'female' ? 'W' : 'all'}
          </button>
        ))}
      </div>
    </div>
  );
};

export default FilterGender;
