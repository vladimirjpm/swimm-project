import React from 'react';
import './training-table.css';
import { rootActions, useAppDispatch, useAppSelector } from '../../store/store';
import { Result } from '../../utils/interfaces/results';

// 👉 добавьте эти импорты своих подкомпонентов
import TrainingTableBySet from './training-table-by-set/training-table-by-set';
import TrainingTableByName from './training-table-by-name/training-table-by-name';
import TrainingShowFullTable from './training-show-full-table/training-show-full-table';
import TrainingTableHeader from './training-table-header/training-table-header';
import Helper from '../../utils/helpers/data-helper';

function TrainingTable() {
  const dispatch = useAppDispatch();
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const filters = useAppSelector((state) => state.filterSelected);

  if (!selectedSource || !selectedSource.results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  // ФИЛЬТРАЦИЯ остаётся здесь, чтобы переиспользовать в любых режимах
  const filteredResults = selectedSource.results.filter((res: Result) => {
    const { pool_type, gender, style_name, style_len, date, age, club, activity_type } = filters;
    const resPoolType = Helper.resolvePoolType(res.pool_type);
    const filterPoolType = pool_type === 'all' ? null : Helper.resolvePoolType(pool_type);
    
    // Фильтр по training/competition
    const hasTraining = !!res.training?.trainingId;
    const activityType = activity_type || 'training';
    if (activityType === 'training' && !hasTraining) return false;
    if (activityType === 'competition' && hasTraining) return false;
    
    return (
      (!filterPoolType || resPoolType === filterPoolType) &&
      (gender === 'all' || res.event_style_gender === gender) &&
      (!style_name || res.event_style_name === style_name) &&
      (!style_len || res.event_style_len === style_len.toString()) &&
      (!date || res.date === date) &&
      (age === 'all' || (
        /^\d+-\d+$/.test(age)
          ? res.age_group === age
          : filters.age_to
            ? Number(res.birth_year) >= Number(age) && Number(res.birth_year) <= Number(filters.age_to)
            : res.birth_year?.toString() === age
      )) &&
      (club === 'all' || res.club === club)
    );
  });

  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(rootActions.updateState({ filterSelected: { ...filters, ...newFilter } }));
  };

  // внутри TrainingTable после filteredResults
  const firstResult = filteredResults[0];
  const uniqueEvents = new Set(filteredResults.map(r => `${r.event_style_name}-${r.event_style_len}`));
  const uniqueDates  = new Set(filteredResults.map(r => r.date));
  const showEvent = uniqueEvents.size > 1;
  const showDate  = uniqueDates.size > 1;

  // РОУТИНГ ПО РЕЖИМУ
  const mode = filters?.training_table?.mode;


  return (
  <div className="training-table results">
    <TrainingTableHeader
      title={selectedSource.title}
      firstResult={firstResult}
      showEvent={showEvent}
      showDate={showDate}
    />

    {mode === 'groupBySet' && (
      <TrainingTableBySet
        results={filteredResults}
        selectedSource={selectedSource}
        filters={filters}
        updateFilter={updateFilter}
        header={{ firstResult, showEvent, showDate }}
      />
    )}

    {mode === 'groupByName' && (
      <TrainingTableByName
        results={filteredResults}
        selectedSource={selectedSource}
        filters={filters}
        updateFilter={updateFilter}
        header={{ firstResult, showEvent, showDate }}
      />
    )}

    {(!mode || mode === 'showTable') && (
      <TrainingShowFullTable
        results={filteredResults}
        selectedSource={selectedSource}
        filters={filters}
        updateFilter={updateFilter}
        header={{ firstResult, showEvent, showDate }}
      />
    )}
  </div>
);
}

export default TrainingTable;
