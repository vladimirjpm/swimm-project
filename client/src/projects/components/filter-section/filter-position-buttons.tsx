import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { TOP_N_POSITIONS } from '../../../utils/constants/filter-constants';

type PositionFilterValue = 'all' | 'top' | 'podium';

interface PositionOption {
  value: PositionFilterValue;
  label: string;
}

const POSITION_OPTIONS: PositionOption[] = [
  { value: 'all', label: 'All' },
  { value: 'top', label: `Top ${TOP_N_POSITIONS}` },
  { value: 'podium', label: '🥇🥈🥉 1-2-3' },
];

const FilterPositionButtons: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const current: PositionFilterValue = filters.position_filter || 'top';

  const updateFilter = (value: PositionFilterValue) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, position_filter: value },
      }),
    );
  };

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Position</h3>
      <div className="flex flex-wrap">
        {POSITION_OPTIONS.map((opt) => (
          <button
            key={opt.value}
            className={`px-2 py-1 m-1 border rounded transition-colors ${
              current === opt.value ? 'theme-btn-active' : 'theme-btn'
            }`}
            onClick={() => updateFilter(opt.value)}
          >
            {opt.label}
          </button>
        ))}
      </div>
    </div>
  );
};

export default FilterPositionButtons;
