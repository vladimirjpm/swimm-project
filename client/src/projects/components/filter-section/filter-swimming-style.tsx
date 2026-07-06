import React, { useMemo } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import { getFilterData } from './filter-types';
import { useFilteredByTypeResults } from './use-filtered-results';
import FilterCard from './filter-card';
import FilterDistance from './filter-distance';

const FilterSwimmingStyle: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filterData = getFilterData();
  const filteredByTypeResults = useFilteredByTypeResults();

  const availableStyleNames = useMemo(() => {
    const set = new Set<string>();
    filteredByTypeResults.forEach((r) => {
      if (r.event_style_name) set.add(r.event_style_name);
    });
    return set;
  }, [filteredByTypeResults]);

  if (!filterData) return null;

  const updateFilter = (style_name: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, style_name },
      }),
    );
  };

  const summary = filters.style_name
    ? `${filters.style_name}${filters.style_len ? ` · ${filters.style_len}m` : ''}`
    : 'All';

  return (
    <FilterCard
      title="Swimming Style"
      summary={summary}
      isActive={!!filters.style_name}
      defaultOpen
    >
      {/* Колонки стиль | дистанция; на узком сайдбаре дистанции переносятся вниз */}
      <div className="flex flex-wrap gap-3">
        <div className="flex flex-col gap-2">
          {filterData.style_list.map((style) => {
            const disabled = !availableStyleNames.has(style.style_name);
            return (
              <button
                key={style.style_name}
                disabled={disabled}
                className={`fseg flex items-center justify-between ${
                  filters.style_name === style.style_name ? 'fseg-active' : ''
                }`}
                onClick={() => !disabled && updateFilter(style.style_name)}
              >
                <UI_SwimmStyleIcon
                  className="w-20"
                  styleName={style.style_name}
                />
              </button>
            );
          })}
        </div>
        {/* Дистанции выбранного стиля — колонка справа от стилей */}
        <FilterDistance />
      </div>
    </FilterCard>
  );
};

export default FilterSwimmingStyle;
