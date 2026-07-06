import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { TOP_N_POSITIONS } from '../../../utils/constants/filter-constants';
import FilterCard from './filter-card';

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

  const currentLabel =
    POSITION_OPTIONS.find((opt) => opt.value === current)?.label || 'All';

  return (
    <FilterCard
      title="Position"
      summary={currentLabel}
      isActive={current !== 'all'}
    >
      <div className="flex flex-wrap gap-2">
        {POSITION_OPTIONS.map((opt) => (
          <button
            key={opt.value}
            className={`fseg ${current === opt.value ? 'fseg-active' : ''}`}
            onClick={() => updateFilter(opt.value)}
          >
            {opt.label}
          </button>
        ))}
      </div>
    </FilterCard>
  );
};

export default FilterPositionButtons;
