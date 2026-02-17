import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';

const FilterResetButton: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);

  const handleReset = () => {
    dispatch(
      rootActions.updateState({
        filterSelected: {
          ...filters,
          date_str: new Date().toISOString().split('T')[0],
          pool_type: 'all',
          gender: 'all',
          style_name: '',
          style_len: 0,
          age: 'all',
          position_filter: 'top',
          level_filter: 'all',
          event_date: 'all',
        },
      }),
    );
  };

  return (
    <button
      className="px-4 py-2 bg-red-500 text-white rounded mt-4"
      onClick={handleReset}
    >
      Reset Filters
    </button>
  );
};

export default FilterResetButton;
