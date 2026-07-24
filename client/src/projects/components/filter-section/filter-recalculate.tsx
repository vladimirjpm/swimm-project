import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { useResultsLoadMode } from '../../../hooks/useResultsLoadMode';

/**
 * Кнопка-тогглер «Recalculate».
 * Пересчитывает позиции по лучшему результату за все дни.
 */
const FilterRecalculate: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const showCombineAllResults = useAppSelector((state) => state.showCombineAllResults);
  const mode = useResultsLoadMode();
  const isActive = !!filters.is_recalculated;

  const toggle = () => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, is_recalculated: !isActive },
      }),
    );
  };

  // Пересчёт требует полного датасета события — в paged v1 недоступен (контракт 3.2 §5).
  return (
    mode !== 'paged' && showCombineAllResults ? (
    <div className="flex flex-col">
      <button
        type="button"
        onClick={toggle}
        className={`fseg flex items-center gap-2 self-start ${
          isActive ? 'fseg-active' : ''
        }`}
        title="Recalculate positions by best result across all days"
      >
        <span>🔄</span>
        <span>Combine All Results</span>
      </button>
    </div>
    ) : null
  );
};

export default FilterRecalculate;
