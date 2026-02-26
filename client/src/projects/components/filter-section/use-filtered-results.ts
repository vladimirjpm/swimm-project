import { useMemo } from 'react';
import { useAppSelector } from '../../../store/store';
import { recalculatePositions } from '../../../utils/helpers/recalculate-positions';

/**
 * Returns the list of results filtered by activity_type (training / competition).
 * When is_recalculated is active, positions are recalculated before filtering.
 * Shared across multiple filter sub-components that derive available options
 * from the currently visible result set.
 */
export function useFilteredByTypeResults() {
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const activityType = useAppSelector(
    (state) => state.filterSelected.activity_type || 'training',
  );
  const isRecalculated = useAppSelector(
    (state) => !!state.filterSelected.is_recalculated,
  );

  return useMemo(() => {
    const raw = selectedSource?.results || [];

    // Применяем recalculation если включён
    const results = isRecalculated
      ? recalculatePositions(raw).map(r => ({
          ...r,
          position_original: r.position,
          position: r.position_recalc,
        }))
      : raw;

    return results.filter((item) => {
      const hasTraining = !!item?.training?.trainingId;
      if (activityType === 'training') return hasTraining;
      if (activityType === 'competition') return !hasTraining;
      return true;
    });
  }, [selectedSource, activityType, isRecalculated]);
}
