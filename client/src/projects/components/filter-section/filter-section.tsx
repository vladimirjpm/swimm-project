import React, { useMemo } from 'react';
import './filter-section.css';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import FilterDateDropdown from './filter-date-ddl';
import FilterNameDropdown from './filter-username-ddl';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import Helper from '../../../utils/helpers/data-helper';
import UI_MedalIcon from '../mix/medal-icon/medal-icon';
import UI_ClubDetails from '../mix/club-details/club-details';
import FilterActivity from './filter-date-training-competition';

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

const FilterSection: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const isDebug = useAppSelector((state) => state.isDebug);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);

  const filter_data = getFilterData();

  // Фильтруем результаты по training/competition
  // ВАЖНО: все хуки должны быть ДО раннего return
  const filteredByTypeResults = useMemo(() => {
    const results = selectedSource?.results || [];
    const activityType = filters.activity_type || 'training';
    
    return results.filter((item) => {
      const hasTraining = !!item?.training?.trainingId;
      if (activityType === 'training') return hasTraining;
      if (activityType === 'competition') return !hasTraining;
      return true;
    });
  }, [selectedSource, filters.activity_type]);

  const availableAges = useMemo(() => {
    const ageSet = new Set<string>();
    filteredByTypeResults.forEach((item) => {
      if (item.event_style_age) {
        ageSet.add(item.event_style_age.toString());
      }
    });
    const sorted = Array.from(ageSet).sort((a, b) => Number(a) - Number(b));
    return ['all', ...sorted];
  }, [filteredByTypeResults]);

  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, ...newFilter },
      }),
    );
  };

  const availableStyleNames = useMemo(() => {
    const set = new Set<string>();
    filteredByTypeResults.forEach((r) => {
      if (r.event_style_name) set.add(r.event_style_name);
    });
    return set;
  }, [filteredByTypeResults]);

  const availableLengths = useMemo(() => {
    const set = new Set<number>();
    if (filters.style_name) {
      filteredByTypeResults.forEach((r) => {
        if (
          r.event_style_name === filters.style_name &&
          r.event_style_len != null
        ) {
          set.add(Number(r.event_style_len));
        }
      });
    }
    return set;
  }, [filteredByTypeResults, filters.style_name]);

  const availableClubs = useMemo(() => {
    return Helper.getClubsSummary(filteredByTypeResults);
  }, [filteredByTypeResults]);

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

  return (
    <div className="dv-filter p-4 rounded-lg theme-bg-section">
      {/* <h2 className="text-lg font-bold mb-2 theme-text-header">Filters</h2> */}

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
              .map(([key, value]) => `<div>${key}: ${value}</div>`)}
          </code>
          
        </div>
      )}
<FilterActivity />
      <FilterDateDropdown />
      <FilterNameDropdown />

      {/* Pool Type */}
      <div className="flex flex-col">
        <h3 className="font-semibold">Pool Type</h3>
        <div className="flex flex-wrap">
          {filter_data.pool_type.map((type) => (
            <button
              key={type}
              className={`px-3 py-1 m-1 border rounded transition-colors ${
                (type === 'all' && filters.pool_type === 'all') ||
                (type !== 'all' &&
                  filters.pool_type !== 'all' &&
                  Helper.resolvePoolType(filters.pool_type) ===
                    Helper.resolvePoolType(type))
                  ? 'theme-btn-active'
                  : 'theme-btn'
              }`}
              onClick={() => updateFilter({ pool_type: type })}
            >
              {type}
            </button>
          ))}
        </div>
      </div>

      {/* Gender */}
      <div className="flex flex-col">
        <h3 className="font-semibold">Gender</h3>
        <div className="flex flex-wrap">
          {filter_data.gender.map((gen) => (
            <button
              key={gen}
              className={`px-3 py-1 m-1 border rounded transition-colors ${
                filters.gender === gen ? 'theme-btn-active' : 'theme-btn'
              }`}
              onClick={() => updateFilter({ gender: gen })}
            >
              {gen === 'male' ? 'M' : gen === 'female' ? 'W' : 'all'}
            </button>
          ))}
        </div>
      </div>

      {/* Age */}
      <div className="flex flex-col">
        <h3 className="font-semibold">Age</h3>
        <div className="flex flex-wrap">
          {availableAges.map((age) => (
            <button
              key={age}
              className={`px-2 py-1 m-1 border rounded transition-colors ${
                filters.age === age ? 'theme-btn-active' : 'theme-btn'
              }`}
              onClick={() => updateFilter({ age })}
            >
              {age === 'all' ? 'all' : age}
            </button>
          ))}
        </div>
      </div>

      {/* Swimming Style */}
      <div className="flex flex-col">
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

      {/* Club Filter */}
      <div className="flex flex-col">
        <h3 className="font-semibold">Club</h3>
        <div className="flex flex-col">
          <div>
            <button
              className={`px-3 py-1 m-1 border rounded transition-colors ${
                filters.club === 'all'
                  ? 'theme-btn-active'
                  : 'theme-btn'
              }`}
              onClick={() => updateFilter({ club: 'all' })}
            >
              all
            </button>
          </div>
          {availableClubs.map(
            ({
              club,
              points,
              swimmerCount,
              successfulCount,
              gold,
              silver,
              bronze,
            }) => (
              <UI_ClubDetails
                key={club}
                club={club}
                isSelected={filters.club === club}
                onSelect={(club) => updateFilter({ club })}
                gold={gold}
                silver={silver}
                bronze={bronze}
                swimmerCount={swimmerCount}
                successfulCount={successfulCount}
                points={points}
              />
            ),
          )}
        </div>
      </div>

      {/* Reset */}
      <button
        className="px-4 py-2 bg-red-500 text-white rounded mt-4"
        onClick={() =>
          updateFilter({
            date_str: new Date().toISOString().split('T')[0],
            pool_type: 'all',
            gender: 'all',
            style_name: '',
            style_len: 0,
            age: 'all',
          })
        }
      >
        Reset Filters
      </button>
    </div>
  );
};

export default FilterSection;
