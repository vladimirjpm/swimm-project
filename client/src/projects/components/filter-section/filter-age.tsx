import React, { useMemo, useRef, useCallback } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { useFilteredByTypeResults } from './use-filtered-results';

const FilterAge: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filteredByTypeResults = useFilteredByTypeResults();
  const isMasters = useAppSelector((state) => !!state.dataSourceSelected?.is_masters);

  const availableAges = useMemo(() => {
    if (isMasters) {
      // Masters mode: collect unique age_group values ("25-29", "30-34", ...)
      const ageGroupSet = new Set<string>();
      filteredByTypeResults.forEach((item) => {
        if (item.age_group) {
          ageGroupSet.add(item.age_group);
        }
      });
      const sorted = Array.from(ageGroupSet).sort(
        (a, b) => Number(a.split('-')[0]) - Number(b.split('-')[0]),
      );
      return {
        birthYears: ['all', ...sorted],
        compYears: [] as number[],
        isMastersMode: true,
      };
    }

    const birthYearSet = new Set<number>();
    filteredByTypeResults.forEach((item) => {
      if (item.birth_year) {
        birthYearSet.add(Number(item.birth_year));
      }
    });
    const sorted = Array.from(birthYearSet).sort((a, b) => a - b);

    const compYears = new Set<number>();
    filteredByTypeResults.forEach((item) => {
      if (item.date) {
        const parts = item.date.includes('/')
          ? item.date.split('/')
          : item.date.split('-');
        const year = item.date.includes('/')
          ? Number(parts[2])
          : Number(parts[0]);
        if (year > 1900) compYears.add(year);
      }
    });
    const compYearsArr = Array.from(compYears).sort((a, b) => a - b);

    return {
      birthYears: ['all', ...sorted.map(String)],
      compYears: compYearsArr,
      isMastersMode: false,
    };
  }, [filteredByTypeResults, isMasters]);

  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, ...newFilter },
      }),
    );
  };

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">
        {availableAges.isMastersMode ? 'Age Group' : 'Age (multi-select with long-press)'}
        {!availableAges.isMastersMode && filters.age_to && filters.age !== 'all' && (
          <span className="ml-2 text-sm font-normal text-blue-600">
            birth year: {filters.age}–{filters.age_to}
          </span>
        )}
      </h3>
      <div className="flex flex-wrap">
        {availableAges.birthYears.map((by) => {
          const isInRange =
            !availableAges.isMastersMode &&
            filters.age !== 'all' &&
            filters.age_to &&
            by !== 'all' &&
            Number(by) >= Number(filters.age) &&
            Number(by) <= Number(filters.age_to);
          const isActive = filters.age === by && !filters.age_to;

          let ageLabel = '';
          if (!availableAges.isMastersMode && by !== 'all' && availableAges.compYears.length > 0) {
            const ages = availableAges.compYears.map(
              (cy) => cy - Number(by),
            );
            const minAge = Math.min(...ages);
            const maxAge = Math.max(...ages);
            ageLabel =
              minAge === maxAge ? `${minAge}` : `${minAge}-${maxAge}`;
          }

          return (
            <AgeButton
              key={by}
              age={by}
              ageLabel={ageLabel}
              isActive={!!isActive}
              isInRange={!!isInRange}
              onClick={() =>
                updateFilter({ age: by, age_to: undefined })
              }
              onLongPress={() => {
                if (
                  filters.age !== 'all' &&
                  by !== 'all' &&
                  filters.age !== by
                ) {
                  const from = Math.min(
                    Number(filters.age),
                    Number(by),
                  );
                  const to = Math.max(
                    Number(filters.age),
                    Number(by),
                  );
                  updateFilter({
                    age: String(from),
                    age_to: String(to),
                  });
                }
              }}
            />
          );
        })}
      </div>
    </div>
  );
};

export default FilterAge;

/* ─── AgeButton with long-press support ─── */

function AgeButton({
  age,
  ageLabel,
  isActive,
  isInRange,
  onClick,
  onLongPress,
}: {
  age: string;
  ageLabel: string;
  isActive: boolean;
  isInRange: boolean;
  onClick: () => void;
  onLongPress: () => void;
}) {
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const didLongPress = useRef(false);

  const startPress = useCallback(() => {
    didLongPress.current = false;
    timerRef.current = setTimeout(() => {
      didLongPress.current = true;
      onLongPress();
    }, 500);
  }, [onLongPress]);

  const endPress = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    if (!didLongPress.current) {
      onClick();
    }
  }, [onClick]);

  const cancelPress = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const cls = isActive
    ? 'theme-btn-active'
    : isInRange
      ? 'theme-btn-active opacity-75'
      : 'theme-btn';

  return (
    <button
      className={`px-2 py-1 m-1 border rounded transition-colors ${cls}`}
      onMouseDown={startPress}
      onMouseUp={endPress}
      onMouseLeave={cancelPress}
      onTouchStart={startPress}
      onTouchEnd={endPress}
      onTouchCancel={cancelPress}
      onContextMenu={(e) => e.preventDefault()}
    >
      {age === 'all' ? (
        'all'
      ) : (
        <span className="flex flex-col">
          {age}
          {ageLabel && (
            <span className="text-xs opacity-70 ml-0.5">
              ({ageLabel})
            </span>
          )}
        </span>
      )}
    </button>
  );
}
