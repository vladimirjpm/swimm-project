import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import UI_NormativeLevelIcon from '../mix/normative-level-icon/normative-level-icon';
import FilterCard from './filter-card';

/**
 * Фильтр по уровню норматива.
 * Две кнопки: All | <самый высокий уровень среди отфильтрованных результатов>
 *
 * Читает bestLevelInfo из store — его вычисляет ResultsTable из baseFilteredResults
 * (все фильтры кроме level_filter), чтобы не было циклической зависимости.
 */
const FilterLevelButtons: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const bestLevel = useAppSelector((state) => state.bestLevelInfo);

  const current = filters.level_filter || 'all';

  const updateFilter = (value: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, level_filter: value },
      }),
    );
  };

  // Если нет результатов с уровнем — не показываем фильтр
  if (!bestLevel) return null;

  return (
    <FilterCard
      title="Best Level"
      summary={current === 'all' ? 'All' : current}
      isActive={current !== 'all'}
    >
      <div className="flex flex-wrap items-center gap-2">
        <button
          className={`fseg ${current === 'all' ? 'fseg-active' : ''}`}
          onClick={() => updateFilter('all')}
        >
          All
        </button>
        <button
          className={`fseg flex items-center gap-1 ${
            current === bestLevel.levelName ? 'fseg-active' : ''
          }`}
          onClick={() => updateFilter(bestLevel.levelName)}
        >
          <UI_NormativeLevelIcon
            levelName={bestLevel.levelName}
            styleType="style-1"
            styleSize="size-1"
            styleName={bestLevel.styleName}
            styleLen={bestLevel.styleLen}
            poolType={bestLevel.poolType}
            isMasters={bestLevel.isMasters}
            disableClick
          />
        </button>
      </div>
    </FilterCard>
  );
};

export default FilterLevelButtons;
