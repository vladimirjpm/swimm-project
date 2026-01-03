import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { ActivityType } from '../../../utils/interfaces/filter-selected';

const FilterActivity: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const dataResults = useAppSelector((state) => state.dataSourceSelected?.results) || [];

  // Проверяем, есть ли хотя бы одна запись с training
  const hasAnyTraining = React.useMemo(() => {
    return dataResults.some((item) => !!item?.training?.trainingId);
  }, [dataResults]);

  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, ...newFilter },
      }),
    );
  };

  // Если нет training записей — не показываем фильтр
  if (!hasAnyTraining) {
    return null;
  }

  return (
    <div className="flex flex-col mb-2">
      <h3 className="font-semibold mb-1">Activity</h3>
      <div className="flex flex-wrap">
        {[
          { label: 'Training', value: 'training' as ActivityType, btnClass: 'btn-training' },
          { label: 'Competition', value: 'competition' as ActivityType, btnClass: 'btn-competition' },
        ].map((opt) => {
          const isActive = (filters.activity_type || 'training') === opt.value;
          return (
            <button
              key={opt.value}
              className={`px-3 py-1 m-1 border rounded transition-colors ${opt.btnClass} ${
                isActive ? 'opacity-100' : 'opacity-50'
              }`}
              onClick={() =>
                updateFilter({
                  activity_type: opt.value,
                })
              }
            >
              {opt.label}
            </button>
          );
        })}
      </div>
    </div>
  );
};

export default FilterActivity;
