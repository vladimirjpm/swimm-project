import React from 'react';
import './filter-section.css';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';

const FilterDateDropdown: React.FC = () => {
  // Хуки ВСЕГДА вызываются безусловно
  const dispatch = useAppDispatch();
  const dataResults = useAppSelector((state) => state.dataSourceSelected?.results) || [];
  const filters = useAppSelector((state) => state.filterSelected);

  // Обновление фильтра
  const updateFilter = React.useCallback((newFilter: Partial<typeof filters>) => {
    dispatch(rootActions.updateState({ filterSelected: { ...filters, ...newFilter } }));
  }, [dispatch, filters]);

  // useMemo ВСЕГДА вызывается безусловно
  const dates: string[] = React.useMemo(() => {
    // Фильтруем результаты по activity_type
    const filteredResults = dataResults.filter((item) => {
      const hasTraining = !!item?.training?.trainingId;
      const activityType = filters.activity_type || 'training';
      
      if (activityType === 'training') return hasTraining;
      if (activityType === 'competition') return !hasTraining;
      return true;
    });

    const uniqueDates = Array.from(new Set(
      filteredResults
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
  }, [dataResults, filters.activity_type]);

  // Храним предыдущее значение activity_type для отслеживания смены
  const prevActivityType = React.useRef(filters.activity_type);
  const hasAutoSelectedRef = React.useRef(false);

  // При смене activity_type автоматически выбираем последнюю дату
  React.useEffect(() => {
    const activityTypeChanged = prevActivityType.current !== filters.activity_type;
    
    if (activityTypeChanged) {
      prevActivityType.current = filters.activity_type;
      hasAutoSelectedRef.current = false; // сбрасываем флаг при смене режима
    }
    
    // Автовыбор последней даты при смене режима, если ещё не выбрали
    if (!hasAutoSelectedRef.current && dates.length > 0) {
      // Проверяем, есть ли текущая дата в списке доступных
      const currentDateValid = filters.date && dates.includes(filters.date);
      
      if (!currentDateValid) {
        hasAutoSelectedRef.current = true;
        const latestDate = dates[0]; // dates уже отсортированы по убыванию
        const [day, month, year] = latestDate.split("/");
        const formattedDate = `${day}-${month}-${year}`;
        dispatch(rootActions.updateState({ 
          filterSelected: { 
            ...filters, 
            date_str: formattedDate,
            date: latestDate,
          } 
        }));
      } else {
        hasAutoSelectedRef.current = true; // текущая дата валидна
      }
    }
  }, [filters.activity_type, dates, filters.date, dispatch]);

  // Если нет дат — не рендерим dropdown
  if (dates.length === 0) return null;

  return (
    <div>
      <h3 className="font-semibold">Date</h3>
      <select
        className="border px-2 py-1 rounded w-full"
        onChange={(e) => {
          const newDateStr = e.target.value;
          
          // Если выбрана пустая опция "-- Select Date --"
          if (!newDateStr) {
            updateFilter({
              date_str: '',
              date: '',
            });
            return;
          }
          
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
