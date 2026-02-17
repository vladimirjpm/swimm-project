import React, { useMemo } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { getFilterData } from './filter-types';
import { useFilteredByTypeResults } from './use-filtered-results';

const FilterDistance: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filterData = getFilterData();
  const filteredByTypeResults = useFilteredByTypeResults();

  const availableLengths = useMemo(() => {
    const set = new Set<number>();
    if (filters.style_name) {
      filteredByTypeResults.forEach((r) => {
        if (
          r.event_style_name === filters.style_name &&
          r.event_style_len != null
        ) {
          set.add(Number(r.event_style_len));
        }
      });
    }
    return set;
  }, [filteredByTypeResults, filters.style_name]);

  if (!filterData || !filters.style_name) return null;

  const styleLens =
    filterData.style_list.find(
      (style) => style.style_name === filters.style_name,
    )?.style_len || [];

  const updateFilter = (style_len: number) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, style_len },
      }),
    );
  };

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Distance</h3>
      {styleLens.map((len) => {
        const disabled = !availableLengths.has(Number(len));
        return (
          <button
            key={len}
            disabled={disabled}
            className={`px-3 py-1 m-1 border rounded transition-colors ${
              filters.style_len === len ? 'theme-btn-active' : 'theme-btn'
            } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
            onClick={() => !disabled && updateFilter(len)}
          >
            {len}m
          </button>
        );
      })}
    </div>
  );
};

export default FilterDistance;
