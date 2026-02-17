import React, { useMemo } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import { getFilterData } from './filter-types';
import { useFilteredByTypeResults } from './use-filtered-results';

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

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Swimming Style</h3>
      {filterData.style_list.map((style) => {
        const disabled = !availableStyleNames.has(style.style_name);
        return (
          <button
            key={style.style_name}
            disabled={disabled}
            className={`px-3 py-1 m-1 border rounded flex items-center justify-between transition-colors ${
              filters.style_name === style.style_name
                ? 'theme-btn-active'
                : 'theme-btn'
            } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
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
  );
};

export default FilterSwimmingStyle;
