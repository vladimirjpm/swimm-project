import React, { useMemo } from 'react';
import './filter-trainig-section.css';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import FilterDateDropdown from './../filter-section/filter-date-ddl';
import FilterNameDropdown from './../filter-section/filter-username-ddl';
import FilterDateTrainingCompetition from './../filter-section/filter-date-training-competition';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';

// тот же тип, что и в FilterSection
type FilterData = {
  pool_type: string[];
  gender: string[];
  style_list: {
    style_name: string;
    style_len: number[];
  }[];
};

// данные приходят из public/data/filter-data.js:
// window.filter_data = { pool_type: [...], gender: [...], style_list: [...] }
const getFilterData = (): FilterData | null => {
  const raw = (window as any).filter_data;
  if (!raw) {
    console.error('filter_data is not loaded from window');
    return null;
  }
  return raw as FilterData;
};

const FilterTrainigSection: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const isDebug = useAppSelector((state) => state.isDebug);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);

  const filter_data = getFilterData();

  // если данных нет — можно показать заглушку или ничего
  if (!filter_data) {
    return (
      <div className="dv-filter-training p-4 bg-gray-100 rounded-lg">
        <h2 className="text-lg font-bold mb-2 text-black">Training Filters</h2>
        <div className="text-sm text-red-600">
          filter_data is not loaded (check script / path)
        </div>
      </div>
    );
  }

  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, ...newFilter },
      }),
    );
  };

  const checkIsTraining = () => {
    const all = selectedSource?.results ?? [];
    if (!all.length) return { isTraining: false, trainingId: null };

    const currentDate = filters?.date;
    const firstForDate = currentDate
      ? all.find((r: any) => r.date === currentDate)
      : all[0];

    const trainingId = firstForDate?.training?.trainingId || null;
    const isTraining = !!trainingId;

    return { isTraining, trainingId };
  };

  const { isTraining, trainingId } = checkIsTraining();

  const availableStyleNames = useMemo(() => {
    const set = new Set<string>();
    selectedSource?.results?.forEach((r) => {
      if (r.event_style_name) set.add(r.event_style_name);
    });
    return set;
  }, [selectedSource]);

  const availableLengths = useMemo(() => {
    const set = new Set<number>();
    if (filters.style_name) {
      selectedSource?.results?.forEach((r) => {
        if (
          r.event_style_name === filters.style_name &&
          r.event_style_len != null
        ) {
          set.add(Number(r.event_style_len));
        }
      });
    }
    return set;
  }, [selectedSource, filters.style_name]);

  return (
    <div className="dv-filter-training p-4 rounded-lg theme-bg-section">

      {isDebug && (
        <div className="text-sm text-gray-700 mb-2">
          <strong>Active Filters: </strong>
          <code>
            {Object.entries(filters)
              .filter(
                ([_, value]) =>
                  value !== 'all' &&
                  value !== '' &&
                  value !== 0 &&
                  value !== undefined &&
                  value !== null,
              )
              .map(([key, value]) => `${key}: ${JSON.stringify(value)}`)}
          </code>

          <div className="mt-2 mb-2 text-sm flex flex-wrap items-center gap-2">
            {isTraining ? (
              <div className="px-3 py-1 rounded bg-green-100 text-green-700 font-medium">
                trainingId: {trainingId}
              </div>
            ) : (
              <div className="px-3 py-1 rounded bg-red-100 text-red-700 font-medium">
                trainingId: no trainingId
              </div>
            )}
          </div>
        </div>
      )}

      {/* Data Type: Training / Competition */}
      <FilterDateTrainingCompetition />

      {/* Dropdowns: Name & Date */}
      <FilterDateDropdown />
      <FilterNameDropdown />

      {/* Training Table Mode */}
      <div className="flex flex-col mt-2">
        <h3 className="font-semibold mb-1">Training Table View</h3>
        <div className="flex flex-col">
          {[
            { label: 'Group by Name', value: 'groupByName' },
            { label: 'Group by Set', value: 'groupBySet' },
            { label: 'Show Table', value: 'showTable' },
          ].map((opt) => (
            <button
              key={opt.value}
              className={`px-3 py-1 m-1 border rounded transition-colors ${
                filters.training_table.mode === opt.value
                  ? 'theme-btn-active'
                  : 'theme-btn'
              }`}
              onClick={() =>
                updateFilter({
                  training_table: {
                    ...filters.training_table,
                    mode: opt.value as any,
                  },
                })
              }
            >
              {opt.label}
            </button>
          ))}
        </div>
      </div>

      {/* Rating buttons: no / regular / masters */}
      <div className="flex flex-col mt-4">
        <h3 className="font-semibold">Rating</h3>
        <div className="mt-2 flex flex-wrap">
          {[
            { label: 'None', value: 'no' },
            { label: 'Masters', value: 'masters' },
          ].map((opt) => (
            <button
              key={opt.value}
              className={`px-2 py-1 m-1 border rounded transition-colors ${
                filters.rating_mode === opt.value
                  ? 'theme-btn-active'
                  : 'theme-btn'
              }`}
              onClick={() => updateFilter({ rating_mode: opt.value as any })}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </div>

      {/* Swimming Style */}
      <div className="flex flex-col mt-4">
        <h3 className="font-semibold">Swimming Style</h3>
        {filter_data.style_list.map((style) => {
          const disabled = !availableStyleNames.has(style.style_name);
          return (
            <button
              key={style.style_name}
              disabled={disabled}
              className={`px-3 py-1 m-1 border rounded flex items-center justify-between transition-colors ${
                filters.style_name === style.style_name
                  ? 'theme-btn-active'
                  : 'theme-btn'
              } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
              onClick={() =>
                !disabled && updateFilter({ style_name: style.style_name })
              }
            >
              <UI_SwimmStyleIcon
                className="w-20"
                styleName={style.style_name}
              />
            </button>
          );
        })}
      </div>

      {/* Distance */}
      {filters.style_name && (
        <div className="flex flex-col">
          <h3 className="font-semibold">Distance</h3>
          {filter_data.style_list
            .find((style) => style.style_name === filters.style_name)
            ?.style_len.map((len) => {
              const disabled = !availableLengths.has(Number(len));
              return (
                <button
                  key={len}
                  disabled={disabled}
                  className={`px-3 py-1 m-1 border rounded transition-colors ${
                    filters.style_len === len
                      ? 'theme-btn-active'
                      : 'theme-btn'
                  } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
                  onClick={() =>
                    !disabled && updateFilter({ style_len: len })
                  }
                >
                  {len}m
                </button>
              );
            })}
        </div>
      )}

      {/* Reset */}
      <button
        className="px-4 py-2 bg-red-500 text-white rounded mt-4"
        onClick={() =>
          updateFilter({
            date_str: new Date().toISOString().split('T')[0],
            style_name: '',
            style_len: 0,
            training_table: { ...filters.training_table, mode: 'groupByName' },
          })
        }
      >
        Reset Filters
      </button>
    </div>
  );
};

export default FilterTrainigSection;
