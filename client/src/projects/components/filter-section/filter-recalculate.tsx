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
        role="switch"
        aria-checked={isActive}
        onClick={toggle}
        className="flex items-center gap-2 self-start bg-transparent p-0 text-[13px] font-bold"
        style={{ color: 'var(--theme-mode-text)' }}
        title="Recalculate positions by best result across all days"
      >
        <span>🔄</span>
        <span>Combine All Results</span>
        {/* Тоггл-свитч: бегунок влево (off) / вправо (on). */}
        <span
          className="relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors"
          style={{ background: isActive ? 'var(--theme-primary)' : 'var(--theme-mode-border)' }}
        >
          <span
            className="inline-block h-4 w-4 rounded-full bg-white shadow transition-transform"
            style={{ transform: isActive ? 'translateX(18px)' : 'translateX(2px)' }}
          />
        </span>
      </button>
    </div>
    ) : null
  );
};

export default FilterRecalculate;
