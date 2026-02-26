import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';

/**
 * Кнопка-тогглер «Recalculate».
 * Пересчитывает позиции по лучшему результату за все дни.
 */
const FilterRecalculate: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const showCombineAllResults = useAppSelector((state) => state.showCombineAllResults);
  const isActive = !!filters.is_recalculated;

  const toggle = () => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, is_recalculated: !isActive },
      }),
    );
  };

  return (
    showCombineAllResults ? (
    <div className="flex flex-col">
      <button
        type="button"
        onClick={toggle}
        className={`px-3 py-1.5 m-1 border rounded transition-colors text-sm font-semibold flex items-center gap-2 ${
          isActive ? 'theme-btn-active ring-2 ring-blue-500' : 'theme-btn'
        }`}
        title="Пересчитать позиции по лучшему результату за все дни"
      >
        <span>🔄</span>
        <span>Combine All Results</span>
      </button>
    </div>
    ) : null
  );
};

export default FilterRecalculate;
