import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import UI_NormativeLevelIcon from '../mix/normative-level-icon/normative-level-icon';

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
    <div className="flex flex-col">
      <h3 className="font-semibold">Level</h3>
      <div className="flex flex-wrap items-center">
        <button
          className={`px-3 py-1 m-1 border rounded transition-colors ${
            current === 'all' ? 'theme-btn-active' : 'theme-btn'
          }`}
          onClick={() => updateFilter('all')}
        >
          All
        </button>
        <button
          className={`px-3 py-1 m-1 border rounded transition-colors flex items-center gap-1 ${
            current === bestLevel.levelName ? 'theme-btn-active' : 'theme-btn'
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
    </div>
  );
};

export default FilterLevelButtons;
