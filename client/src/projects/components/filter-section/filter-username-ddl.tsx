import React from 'react';
import Select from 'react-select';
import './filter-section.css';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';
import FilterCard from './filter-card';
import { useResultsLoadMode } from '../../../hooks/useResultsLoadMode';
import { useFilterHints } from '../../../hooks/useFilterHints';

const ALL_OPTION = { value: 'all', label: '-- All Names --' };

const FilterNameDropdown: React.FC = () => {
  const dispatch = useAppDispatch();
  const dataResults = useAppSelector((state) => state.dataSourceSelected?.results) || [];
  const filters = useAppSelector((state) => state.filterSelected);
  const mode = useResultsLoadMode();

  // Список уникальных имён (full-режим — из загруженного датасета)
  const nameOptions = React.useMemo(() => {
    const fullNames = dataResults.map(item => {
      const first = item.first_name?.trim();
      const last = item.last_name?.trim();
      if (first && last) return `${first} ${last}`;
      if (last) return last;
      if (first) return first;
      return null;
    }).filter((name): name is string => Boolean(name));

    const uniqueNames = Array.from(new Set(fullNames)).sort((a, b) => a.localeCompare(b));

    return [ALL_OPTION, ...uniqueNames.map(name => ({ value: name, label: name }))];
  }, [dataResults]);

  // Paged: полного датасета нет — опции по мере ввода из filter-hints (контракт 3.2 §4).
  const [nameQuery, setNameQuery] = React.useState('');
  const nameHints = useFilterHints('name', nameQuery, 20, mode === 'paged');
  const pagedOptions = React.useMemo(
    () => [ALL_OPTION, ...nameHints.map(name => ({ value: name, label: name }))],
    [nameHints],
  );

  const options = mode === 'paged' ? pagedOptions : nameOptions;

  // Найти текущий selected option
  const selectedOption = options.find(opt => opt.value === filters.selected_name)
    || (filters.selected_name && filters.selected_name !== 'all'
      ? { value: filters.selected_name, label: filters.selected_name }
      : ALL_OPTION);

  const handleChange = (selected: { value: string; label: string } | null) => {
    dispatch(rootActions.updateState({
      filterSelected: {
        ...filters,
        selected_name: selected?.value || 'all'
      }
    }));
  };

  if (mode !== 'paged' && nameOptions.length <= 1) return null;

  const isActive = !!filters.selected_name && filters.selected_name !== 'all';

  return (
    <FilterCard
      title="Name"
      summary={isActive ? filters.selected_name : 'All'}
      isActive={isActive}
    >
      <Select
        options={options}
        value={selectedOption}
        onChange={handleChange}
        // Paged: сервер делает prefix-поиск по q — не дублируем фильтрацию списка на клиенте.
        {...(mode === 'paged'
          ? { onInputChange: (v: string) => setNameQuery(v), filterOption: () => true }
          : {})}
        isClearable
        classNamePrefix="fname"
        // Портал в body: FilterCard (overflow-hidden) и скролл drawer-а резали меню.
        menuPortalTarget={typeof document !== 'undefined' ? document.body : undefined}
        styles={{ menuPortal: (base) => ({ ...base, zIndex: 60 }) }}
      />
    </FilterCard>
  );
};

export default FilterNameDropdown;
