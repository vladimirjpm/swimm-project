import React, { useMemo } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import UI_DateIcon from '../mix/date-icon/date-icon';
import UI_PrelimLabel from '../mix/prelim-label/prelim-label';
import { useFilteredByTypeResults } from './use-filtered-results';
import FilterCard from './filter-card';
import { useResultsLoadMode } from '../../../hooks/useResultsLoadMode';

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
  const mode = useResultsLoadMode();
  const dayDates = useAppSelector((state) => state.dataSourceSelected?.day_dates);

  const current = filters.event_date || 'all';

  const uniqueDates = useMemo(() => {
    // Paged: страница (сортировка по дате DESC) обычно покрывает один день события —
    // опции дней берём из day_dates источника (/api/competitions), а не из строк.
    if (mode === 'paged' && dayDates?.length) return dayDates;
    const set = new Set<string>();
    filteredByTypeResults.forEach((r) => {
      if (r.date) set.add(r.date);
    });
    return Array.from(set).sort(
      (a, b) => parseDateDMY(a).getTime() - parseDateDMY(b).getTime(),
    );
  }, [mode, dayDates, filteredByTypeResults]);

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

  // Если только одна дата — выбор даты не нужен; но если в данных есть предварительные
  // заплывы, карточка остаётся ради тумблера [prelim] — иначе на одиночном дне
  // соревнования с прелимами их никак не включить.
  const hasPrelims = filteredByTypeResults.some((r) => r.heat_type === 'prelim');
  if (uniqueDates.length <= 1 && !hasPrelims) return null;

  return (
    <FilterCard
      title="Date"
      summary={current === 'all' ? 'All' : current}
      isActive={current !== 'all'}
      defaultOpen
    >
      <div className="flex flex-wrap items-center gap-2">
        <button
          className={`fseg ${current === 'all' ? 'fseg-active' : ''}`}
          onClick={() => updateFilter('all')}
        >
          All
        </button>

        {uniqueDates.map((date) => (
          <button
            key={date}
            className={`fseg !p-0 overflow-hidden ${
              current === date ? 'fseg-active' : ''
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

        {/* Предварительные заплывы (heat_type='prelim') по умолчанию скрыты — официальный
            вид это финалы. Тумблер есть только у соревнований с прелимами: выключен —
            [has prelim] («они тут есть»), включён — зелёный [prelim ON]. Тексты и цвета —
            в UI_PrelimLabel; сводка сверху дублирует состояние под датой. */}
        {hasPrelims && (
          <button
            className="fseg"
            onClick={() =>
              dispatch(
                rootActions.updateState({
                  filterSelected: { ...filters, show_prelims: !filters.show_prelims },
                }),
              )
            }
          >
            <UI_PrelimLabel state={filters.show_prelims ? 'on' : 'has'} className="text-[12px] normal-case" />
          </button>
        )}
      </div>
    </FilterCard>
  );
};

export default FilterEventDate;
