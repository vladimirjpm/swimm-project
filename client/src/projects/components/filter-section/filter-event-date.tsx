import React, { useMemo } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import UI_DateIcon from '../mix/date-icon/date-icon';
import { useFilteredByTypeResults } from './use-filtered-results';

/**
 * Парсит дату формата DD/MM/YYYY в объект Date для сортировки.
 */
const parseDateDMY = (d: string): Date => {
  const [day, month, year] = d.split('/').map(Number);
  return new Date(year, month - 1, day);
};

/**
 * Фильтр по дате события.
 * Кнопки: All | <каждая уникальная дата из результатов в виде UI_DateIcon>
 */
const FilterEventDate: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filteredByTypeResults = useFilteredByTypeResults();

  const current = filters.event_date || 'all';

  const uniqueDates = useMemo(() => {
    const set = new Set<string>();
    filteredByTypeResults.forEach((r) => {
      if (r.date) set.add(r.date);
    });
    return Array.from(set).sort(
      (a, b) => parseDateDMY(a).getTime() - parseDateDMY(b).getTime(),
    );
  }, [filteredByTypeResults]);

  const updateFilter = (value: string) => {
    if (value === 'all') {
      dispatch(
        rootActions.updateState({
          filterSelected: { ...filters, event_date: 'all', date: '', date_str: '' },
        }),
      );
    } else {
      const [day, month, year] = value.split('/');
      const formattedDate = `${day}-${month}-${year}`;
      dispatch(
        rootActions.updateState({
          filterSelected: { ...filters, event_date: value, date: value, date_str: formattedDate },
        }),
      );
    }
  };

  // Если только одна дата — фильтр не нужен
  if (uniqueDates.length <= 1) return null;

  return (
    <div className="flex flex-col">
      <h3 className="font-semibold">Date</h3>
      <div className="flex flex-wrap items-center gap-1">
        <button
          className={`px-3 py-1 m-1 border rounded transition-colors ${
            current === 'all' ? 'theme-btn-active' : 'theme-btn'
          }`}
          onClick={() => updateFilter('all')}
        >
          All
        </button>

        {uniqueDates.map((date) => (
          <button
            key={date}
            className={`p-0 m-1 border rounded transition-colors overflow-hidden ${
              current === date ? 'theme-btn-active ring-2 ring-blue-500' : 'theme-btn'
            }`}
            onClick={() => updateFilter(date)}
          >
            <UI_DateIcon
              styleType="cube"
              date={date}
              className="text-xs"
              paddingClass="px-1 py-1"
            />
          </button>
        ))}
      </div>
    </div>
  );
};

export default FilterEventDate;
