import { useMemo } from 'react';
import { useAppSelector } from '../../../store/store';
import { Result } from '../../../utils/interfaces/results';
import { applyCombinedPositions } from '../../../utils/helpers/recalculate-positions';

/** Стабильная пустая выборка: возвращать новый [] каждый раз — значит рвать мемоизацию у всех,
 *  кто держит результат в зависимостях. */
const EMPTY_RESULTS: Result[] = [];

/**
 * Returns the list of results filtered by activity_type (training / competition).
 * When is_recalculated is active, positions are recalculated before filtering.
 * Shared across multiple filter sub-components that derive available options
 * from the currently visible result set.
 *
 * `enabled = false` — вызов вхолостую: выборка не нужна, но правило хуков требует вызвать
 * хук всё равно (так делает «спящий» `useReduxFilterHost`). Возвращается стабильный пустой
 * массив, прохода по результатам НЕ происходит.
 */
export function useFilteredByTypeResults(enabled = true) {
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const activityType = useAppSelector(
    (state) => state.filterSelected.activity_type || 'training',
  );
  const isRecalculated = useAppSelector(
    (state) => !!state.filterSelected.is_recalculated,
  );

  return useMemo(() => {
    if (!enabled) return EMPTY_RESULTS;

    const raw = selectedSource?.results || [];

    // Применяем recalculation если включён
    const results = isRecalculated ? applyCombinedPositions(raw) : raw;

    return results.filter((item) => {
      const hasTraining = !!item?.training?.trainingId;
      if (activityType === 'training') return hasTraining;
      if (activityType === 'competition') return !hasTraining;
      return true;
    });
  }, [enabled, selectedSource, activityType, isRecalculated]);
}
