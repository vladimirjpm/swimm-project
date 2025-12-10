import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { FilterDateTrainingCompetition as FilterDateTrainingCompetitionType } from '../../../utils/interfaces/filter-selected';

const FilterDateTrainingCompetition: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);

  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, ...newFilter },
      }),
    );
  };

  return (
    <div className="flex flex-col mb-2">
      <h3 className="font-semibold mb-1">Date Filter</h3>
      <div className="flex flex-wrap">
        {[
          { label: 'Training', value: 'training' as FilterDateTrainingCompetitionType },
          { label: 'Competition', value: 'competition' as FilterDateTrainingCompetitionType },
        ].map((opt) => (
          <button
            key={opt.value}
            className={`px-3 py-1 m-1 border rounded transition-colors ${
              (filters.filter_date_training_competition || 'training') === opt.value
                ? 'theme-btn-active'
                : 'theme-btn'
            }`}
            onClick={() =>
              updateFilter({
                filter_date_training_competition: opt.value,
              })
            }
          >
            {opt.label}
          </button>
        ))}
      </div>
    </div>
  );
};

export default FilterDateTrainingCompetition;
