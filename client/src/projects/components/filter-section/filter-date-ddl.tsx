import React from 'react';
import './filter-section.css';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';

const FilterDateDropdown: React.FC = () => {
  // Хуки ВСЕГДА вызываются безусловно
  const dispatch = useAppDispatch();
  const dataResults = useAppSelector((state) => state.dataSourceSelected?.results) || [];
  const filters = useAppSelector((state) => state.filterSelected);

  // useMemo ВСЕГДА вызывается безусловно
  const dates: string[] = React.useMemo(() => {
    const uniqueDates = Array.from(new Set(
      dataResults
        .map(item => item?.date)
        .filter((d): d is string => Boolean(d))
    ));

    return uniqueDates.sort((a, b) => {
      const parse = (d: string) => {
        const [day, month, year] = d.split("/");
        return new Date(`${year}-${month}-${day}`);
      };
      return parse(b).getTime() - parse(a).getTime();
    });
  }, [dataResults]);

  // Обновление фильтра
  const updateFilter = (newFilter: Partial<typeof filters>) => {
    dispatch(rootActions.updateState({ filterSelected: { ...filters, ...newFilter } }));
  };

  // Если нет дат — не рендерим dropdown
  if (dates.length === 0) return null;

  return (
    <div>
      <h3 className="font-semibold">Date</h3>
      <select
        className="border px-2 py-1"
        onChange={(e) => {
          const newDateStr = e.target.value;
          const [day, month, year] = newDateStr.split("/");
          const formattedDate = `${day}-${month}-${year}`;

          updateFilter({
            date_str: formattedDate,
            date: newDateStr,
          });
        }}
        value={filters.date}
      >
        <option value="">-- Select Date --</option>
        {dates.map(date => (
          <option key={date} value={date}>{date}</option>
        ))}
      </select>
    </div>
  );
};

export default FilterDateDropdown;
