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
          event_category: 'all',
          show_prelims: false,
        },
      }),
    );
  };

  return (
    <button
      className="self-start px-4 py-[9px] rounded-[10px] text-[13px] font-bold text-white bg-[#e63946] hover:bg-[#d12d3a] transition-colors"
      onClick={handleReset}
    >
      Reset Filters
    </button>
  );
};

export default FilterResetButton;
