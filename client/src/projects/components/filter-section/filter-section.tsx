import React from 'react';
import './filter-section.css';
import { useAppSelector } from '../../../store/store';
import FilterDateDropdown from './filter-date-ddl';
import FilterNameDropdown from './filter-username-ddl';
import FilterActivity from './filter-date-training-competition';
import FilterPositionButtons from './filter-position-buttons';
import FilterLevelButtons from './filter-level-buttons';
import FilterResetButton from './filter-reset-button';
import FilterPoolType from './filter-pool-type';
import FilterGender from './filter-gender';
import FilterAge from './filter-age';
import FilterSwimmingStyle from './filter-swimming-style';
import FilterClub from './filter-club';
import FilterEventDate from './filter-event-date';
import FilterEventCategory from './filter-event-category';
import { getFilterData } from './filter-types';

const FilterSection: React.FC = () => {
  const filters = useAppSelector((state) => state.filterSelected);
  const isDebug = useAppSelector((state) => state.isDebug);
  const filterData = getFilterData();

  if (!filterData) {
    return (
      <div className="dv-filter-training p-4 bg-[var(--theme-mode-surface-alt)] rounded-lg">
        <h2 className="text-lg font-bold mb-2 text-[var(--theme-mode-text)]">Training Filters</h2>
        <div className="text-sm text-red-600">
          filter_data is not loaded (check script / path)
        </div>
      </div>
    );
  }

  return (
    <div className="dv-filter p-4 rounded-lg">
      {isDebug && (
        <div className="text-sm text-[var(--theme-mode-text-secondary)] mb-2">
          <strong>Active Filters: </strong>
          <code>
            {Object.entries(filters)
              .filter(
                ([_, value]) =>
                  value !== 'all' &&
                  value !== '' &&
                  value !== 0 &&
                  value !== undefined &&
                  value !== null,
              )
              .map(([key, value]) => `<div>${key}: ${value}</div>`)}
          </code>
        </div>
      )}

      <FilterActivity />
      <FilterResetButton />
      <FilterSwimmingStyle />
      <FilterEventDate />
      <FilterEventCategory />
      {/* Combine All Results в фильтрах больше нет: он стал полосой под табами шапки
          соревнования на всех брейкпоинтах (combine-bar.tsx, handoff 12b). */}
      {/* <FilterDateDropdown /> */}
      <FilterNameDropdown />
      <FilterPositionButtons />
      <FilterLevelButtons />
      <FilterPoolType />
      <FilterGender />
      <FilterAge />
      <FilterClub />
    </div>
  );
};

export default FilterSection;
