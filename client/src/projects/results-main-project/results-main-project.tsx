import React, { useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import '../../index.css'; // общий Tailwind + твои базовые стили
import './results-main-project.css';
import { useAppSelector } from '../../store/store';
import FilterSection from '../components/filter-section/filter-section';
import ResultsTable from '../results-table/results-table';
import SportsmenDetails from '../sportsmen-details/sportsmen-details';
import Popup from '../components/popup/popup';
import TrainingTable from '../training-table/training-table';
import FilterTrainigSection from '../components/filter-trainig-section/filter-trainig-section';
import DataSourceDDL from '../components/filter-data-source-ddl/filter-data-source-ddl';
import { useTheme } from '../../hooks/useTheme';

// === Вспомогательная функция ===
function checkIsTraining(selectedSource: any, filters: any) {
  // Используем activity_type из фильтров как основной источник истины
  const activityType = filters?.activity_type || 'training';
  const isTraining = activityType === 'training';

  // Если нужен trainingId, ищем его в данных
  const all = selectedSource?.results ?? [];
  if (!all.length) return { isTraining, trainingId: null };

  const currentDate = filters?.date;
  const firstForDate = currentDate
    ? all.find((r: any) => r.date === currentDate)
    : all.find((r: any) => !!r?.training?.trainingId === isTraining); // ищем запись соответствующую режиму

  const trainingId = firstForDate?.training?.trainingId || null;

  return { isTraining, trainingId };
}

// Краткое описание выбранных фильтров для узкой полоски
function summarizeFilters(filters: any, isTraining: boolean) {
  if (!filters) return 'All results';
  if (isTraining) {
    const mode = filters?.training_table?.mode ?? 'default';
    const lane = filters?.training_table?.lane ?? 'all lanes';
    const grp = filters?.training_table?.group ?? 'all groups';
    return `training: ${mode}, ${lane}, ${grp}`;
  }
  const parts: string[] = [];
  if (filters.selected_name && filters.selected_name !== 'all') parts.push(filters.selected_name);
  if (filters.style_name && filters.style_name !== 'all') parts.push(filters.style_name);
  if (filters.style_len && filters.style_len !== 'all') parts.push(`${filters.style_len}m`);
  if (filters.gender && filters.gender !== 'all') parts.push(filters.gender);
  if (filters.pool_type && filters.pool_type !== 'all') parts.push(filters.pool_type);
  if (filters.date_str) parts.push(filters.date_str);
  return parts.length ? parts.join(' • ') : 'All results';
}

/** ВЕРХНЯЯ шторка (DDL источника) — ВСЕГДА фиксирована сверху и через портал */
function MobileSourceDrawer({
  rowsCount,
  children,
}: {
  rowsCount: number;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(false);

  // Содержимое, которое уйдёт в <body> через портал
  const node = (
    <div className="lg:hidden fixed top-0 left-0 right-0 z-[100]">
      <button
        type="button"
        onClick={() => setOpen(v => !v)}
        className="w-full flex items-center justify-between border-b bg-white/95 backdrop-blur px-3 py-1 text-xs font-medium text-gray-700"
        style={{ minHeight: 'var(--mobile-topbar-h)' }}
        aria-expanded={open}
        aria-controls="mobile-source-panel"
      >
        <span className="truncate">{rowsCount.toLocaleString()} rows</span>
        <span className="ml-2 inline-block leading-none">{open ? '▴' : '▾'}</span>
      </button>

      <div
        id="mobile-source-panel"
        className={`overflow-hidden1 transition-all duration-300 ease-out bg-white border-b shadow-sm ${
          open ? 'max-h-[70vh] p-3' : 'max-h-0 p-0'
        }`}
      >
        {open && <div className="w-full">{children}</div>}
      </div>
    </div>
  );

  return <>{createPortal(node, document.body)}</>;
}

/** НИЖНЯЯ шторка (фильтры) — ВСЕГДА фиксирована снизу и через портал */
function MobileFiltersDrawer({
  summary,
  children,
}: {
  summary: string;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(false);

  const node = (
    <>
      {/* мобильная плавающая кнопка Filters (центрированная) */}
      <button
        type="button"
        onClick={() => setOpen(v => !v)}
        className="lg:hidden fixed bottom-4 left-1/2 z-[100] transform -translate-x-1/2 bg-blue-600 text-white px-5 py-2 rounded-full shadow-lg flex items-center gap-3"
        aria-expanded={open}
        aria-controls="mobile-filters-sheet"
        title="Filters"
      >
        <span className="font-medium">Filters</span>
        <span className="text-sm opacity-80">{open ? '▾' : '▴'}</span>
      </button>

      {/* затемнение */}
      
      <div
        id="mobile-filters-sheet"
        className={`fixed inset-0 z-[ ninety ] bg-black/20 transition-opacity ${
          open ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none'
        }`}
        onClick={() => setOpen(false)}
      />

      {/* сама панель */}
      <div
        className={`fixed bottom-0 left-0 right-0 z-[100] bg-white shadow-2xl rounded-t-2xl transition-transform duration-300 ease-out
        ${open ? 'translate-y-0' : 'translate-y-[calc(100%_-_2px)]'}`}
        aria-hidden={!open}
      >
        <div
          className="flex items-center justify-between px-4 py-2 border-b cursor-pointer"
          onClick={() => setOpen(false)}
        >
          Apply & Close<div className="h-2.5 w-12 rounded-full bg-gray-300 mx-auto" />Apply & Close
        </div>
        <div className="max-h-[85vh] overflow-y-auto p-3" onClick={(e) => e.stopPropagation()}>
          {children}
          <div className="h-24" />
        </div>
      </div>
    </>
  );

  return <>{createPortal(node, document.body)}</>;
}


function ResultsMain() {
  const isPopup = useAppSelector((state) => state.isPopup);
  const popUpType = useAppSelector((state) => state.popUpType);
  const filters = useAppSelector((state) => state.filterSelected);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);

  // Активируем тему на уровне главного компонента
  useTheme();

  const { isTraining } = checkIsTraining(selectedSource, filters);

  // === Проверка, выбран ли источник ===
  const hasSource =
    selectedSource &&
    selectedSource.results &&
    selectedSource.results.length > 0;

  const rowsCount = useMemo(
    () => (selectedSource?.results?.length ?? 0),
    [selectedSource]
  );

  const filtersSummary = useMemo(
    () => summarizeFilters(filters, isTraining),
    [filters, isTraining]
  );

  return (
    <div className="dolphine-training p-4 pt-safe pb-safe">
      {/* Мобильная верхняя шторка (DDL источника) */}
      <MobileSourceDrawer rowsCount={rowsCount}>
        <DataSourceDDL />
      </MobileSourceDrawer>

      {/* Десктопный верхний блок с DDL (как было), скрыт на мобильных */}
      <div className="w-full z-40 hidden lg:block">
        <DataSourceDDL />
      </div>

      {/* Если источник не выбран */}
      {!hasSource ? (
        <div className="mt-6 p-6 bg-yellow-100 border border-yellow-300 rounded text-center text-gray-700 font-medium">
          ⚠️ Источник данных не выбран. Пожалуйста, выберите источник из списка выше.
        </div>
      ) : (
        <>
          {/* Контент страницы */}
          <div className="flex flex-col lg:flex-row gap-4 mt-4">
            {/* Левая колонка — фильтры (только десктоп) */}
            {!isTraining && (
              <div className="filter-section w-full lg:w-2/12 hidden lg:block">
                <FilterSection />
              </div>
            )}
            {isTraining && (
              <div className="filter-trainig-section w-full lg:w-2/12 hidden lg:block">
                <FilterTrainigSection />
              </div>
            )}

            {/* Центральная колонка — результаты */}
            {!isTraining && (
              <div className="results-table w-full lg:w-6/12 bg-white p-4 rounded shadow">
                <ResultsTable />
              </div>
            )}
            {isTraining && (
              <div className="w-full lg:w-6/12 bg-white p-0 md:p-4 rounded shadow">
                <TrainingTable />
              </div>
            )}

            {/* Правая колонка — детали спортсмена (только десктоп) */}
            <section className="section-sportsmen-details hidden lg:block w-full lg:w-4/12 bg-white p-4 rounded shadow relative">
              {filters?.selected_name && filters.selected_name !== 'all' && (
                <SportsmenDetails />
              )}
            </section>

            {/* POPUP */}
            {isPopup && popUpType === 'normative' && <Popup />}
          </div>

          {/* Мобильная нижняя шторка с фильтрами (во весь экран при раскрытии) */}
          <MobileFiltersDrawer summary={filtersSummary}>
            {!isTraining ? <FilterSection /> : <FilterTrainigSection />}
          </MobileFiltersDrawer>

          {/* Мобильный модал с деталями спортсмена (показывается вместо правой колонки на моб/планшет) */}
          <MobileSportsmenModal selectedName={filters?.selected_name && filters.selected_name !== 'all' ? filters.selected_name : null}>
            {filters?.selected_name && filters.selected_name !== 'all' && <SportsmenDetails />}
          </MobileSportsmenModal>
        </>
      )}
    </div>
  );
}

  /** МОБИЛЬНЫЙ МОДАЛ С ДЕТАЛЯМИ СПОРТСМЕНА */
  function MobileSportsmenModal({
    selectedName,
    children,
  }: {
    selectedName?: string | null;
    children: React.ReactNode;
  }) {
    const [open, setOpen] = useState(false);

    // открываем автоматом, когда выбранное имя меняется на truthy; закрываем если имя снято
    React.useEffect(() => {
      if (selectedName) setOpen(true);
      else setOpen(false);
    }, [selectedName]);

    const node = (
      <>
        {/* оверлей с содержимым (модал снизу) */}
        {open &&
          createPortal(
            <div
              className="fixed inset-0 z-[120] bg-black/50 flex items-end lg:hidden"
              onClick={() => setOpen(false)}
            >
              <div
                className="w-full max-h-[85vh] bg-white rounded-t-2xl shadow-xl overflow-auto p-4"
                onClick={(e) => e.stopPropagation()}
              >
                <div className="flex justify-between items-center mb-3">
                  <div className="h-1.5 w-12 rounded-full bg-gray-300 mx-auto" />
                  <button onClick={() => setOpen(false)} className="text-xl">×</button>
                </div>
                {children}
              </div>
            </div>,
            document.body
          )}
      </>
    );

    return <>{node}</>;
  }
export default ResultsMain;
