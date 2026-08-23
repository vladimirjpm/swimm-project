import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import {
  POSITION_OPTIONS,
  PositionFilterValue,
  getPositionLabel,
} from '../../../utils/constants/position-filter';
import FilterCard from './filter-card';

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

  const currentLabel = getPositionLabel(current);

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
