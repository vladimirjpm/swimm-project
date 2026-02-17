import { useMemo } from 'react';
import { useAppSelector } from '../../../store/store';

/**
 * Returns the list of results filtered by activity_type (training / competition).
 * Shared across multiple filter sub-components that derive available options
 * from the currently visible result set.
 */
export function useFilteredByTypeResults() {
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const activityType = useAppSelector(
    (state) => state.filterSelected.activity_type || 'training',
  );

  return useMemo(() => {
    const results = selectedSource?.results || [];
    return results.filter((item) => {
      const hasTraining = !!item?.training?.trainingId;
      if (activityType === 'training') return hasTraining;
      if (activityType === 'competition') return !hasTraining;
      return true;
    });
  }, [selectedSource, activityType]);
}
