import React from 'react';
import Select from 'react-select';
import './filter-section.css';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';
import FilterCard from './filter-card';

const FilterNameDropdown: React.FC = () => {
  const dispatch = useAppDispatch();
  const dataResults = useAppSelector((state) => state.dataSourceSelected?.results) || [];
  const filters = useAppSelector((state) => state.filterSelected);

  // Список уникальных имён
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

    return [
      { value: 'all', label: '-- All Names --' },
      ...uniqueNames.map(name => ({ value: name, label: name }))
    ];
  }, [dataResults]);

  // Найти текущий selected option
  const selectedOption = nameOptions.find(opt => opt.value === filters.selected_name) || nameOptions[0];

  const handleChange = (selected: { value: string; label: string } | null) => {
    dispatch(rootActions.updateState({
      filterSelected: {
        ...filters,
        selected_name: selected?.value || 'all'
      }
    }));
  };

  if (nameOptions.length <= 1) return null;

  const isActive = !!filters.selected_name && filters.selected_name !== 'all';

  return (
    <FilterCard
      title="Name"
      summary={isActive ? filters.selected_name : 'All'}
      isActive={isActive}
    >
      <Select
        options={nameOptions}
        value={selectedOption}
        onChange={handleChange}
        isClearable
        classNamePrefix="fname"
      />
    </FilterCard>
  );
};

export default FilterNameDropdown;
