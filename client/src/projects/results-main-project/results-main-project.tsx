import React, { useMemo, useState, useCallback, useEffect } from 'react';
import { createPortal } from 'react-dom';
import '../../index.css'; // общий Tailwind + твои базовые стили
import './results-main-project.css';
import { useAppSelector, useAppDispatch, rootActions } from '../../store/store';
import FilterSection from '../components/filter-section/filter-section';
import ResultsTable from '../results-table/results-table';
import SportsmenDetails from '../sportsmen-details/sportsmen-details';
import Popup from '../components/popup/popup';
import TrainingTable from '../training-table/training-table';
import FilterTrainigSection from '../components/filter-trainig-section/filter-trainig-section';
import DataSourceDDL from '../components/filter-data-source-ddl/filter-data-source-ddl';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_ThemeDevTool from '../components/mix/theme-dev-tool/theme-dev-tool';
import GroupHeader from './components/group-header/group-header';
import AppTopbar from '../components/app-topbar/app-topbar';
import type { HubGroupDetails } from '../hub-groups-project/types';

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
      {/* мобильная плавающая кнопка Filters / Apply (центрированная, всегда на месте) */}
      <button
        type="button"
        onClick={() => setOpen(v => !v)}
        className="lg:hidden fixed bottom-4 left-1/2 z-[110] transform -translate-x-1/2 bg-[var(--theme-primary)] hover:bg-[var(--theme-primary-hover)] text-white px-5 py-2 rounded-full shadow-lg flex items-center gap-3 transition-colors"
        aria-expanded={open}
        aria-controls="mobile-filters-sheet"
        title={open ? 'Apply' : 'Filters'}
      >
        <span className="font-medium">{open ? 'Apply' : 'Filters'}</span>
        <span className="text-sm opacity-80">{open ? '▾' : '▴'}</span>
      </button>

      {/* затемнение */}
      
      <div
        id="mobile-filters-sheet"
        className={`fixed inset-0 z-[90] bg-black/20 transition-opacity ${
          open ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none'
        }`}
        onClick={() => setOpen(false)}
      />

      {/* сама панель — от самого верха до низа */}
      <div
        className={`fixed inset-0 z-[100] bg-[var(--theme-mode-surface)] text-[var(--theme-mode-text)] shadow-2xl transition-transform duration-300 ease-out
        ${open ? 'translate-y-0' : 'translate-y-full'}`}
        aria-hidden={!open}
      >
        <div className="h-full overflow-y-auto p-3 pb-24" onClick={(e) => e.stopPropagation()}>
          {children}
        </div>
      </div>
    </>
  );

  return <>{createPortal(node, document.body)}</>;
}


function ResultsMain() {
  const dispatch = useAppDispatch();
  const isPopup = useAppSelector((state) => state.isPopup);
  const popUpType = useAppSelector((state) => state.popUpType);
  const filters = useAppSelector((state) => state.filterSelected);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);

  // Активируем тему на уровне главного компонента
  useTheme();
  // Активируем ось режима Light/Dark (data-mode на <html>)
  useMode();

  const { isTraining } = checkIsTraining(selectedSource, filters);

  // Единый ХАБ группы: ?group=<slug>[&tab=trainings|competitions] (default competitions).
  // Competitions — публичные результаты ростера (заужение /api/results на HubGroupMembers),
  // Trainings — приватный Sys_-источник (доступ решает сервер: владелец/админ/участник-аккаунт).
  const groupParams = new URLSearchParams(window.location.search);
  const groupSlug = groupParams.get('group');
  const [groupTab, setGroupTab] = useState<'trainings' | 'competitions'>(
    groupParams.get('tab') === 'trainings' ? 'trainings' : 'competitions',
  );
  const [groupInfo, setGroupInfo] = useState<HubGroupDetails | null>(null);
  const [groupError, setGroupError] = useState<string | null>(null);

  useEffect(() => {
    if (!groupSlug) return;
    let cancelled = false;

    const loadTrainings = async () => {
      const r = await fetch(`/api/hub-groups/${encodeURIComponent(groupSlug)}/trainings`, {
        credentials: 'include',
      });
      if (cancelled) return;
      if (r.status === 401) { setGroupError('Sign in as a group member to view trainings.'); return; }
      if (r.status === 403) { setGroupError('Trainings are private — only group members can view them.'); return; }
      if (r.status === 404) { setGroupError('Group not found.'); return; }
      if (!r.ok) { setGroupError(`Failed to load trainings (${r.status}).`); return; }
      const data = await r.json();
      setGroupError(null);
      dispatch(rootActions.updateState({
        dataSourceSelected: data,
        // Сбрасываем ограничивающие дефолты (date=сегодня, style=freestyle/50), иначе
        // тренировки с датой «28/10/2025» отфильтровываются в ноль. Показываем всё, дальше юзер фильтрует.
        filterSelected: {
          ...filters,
          activity_type: 'training',
          date: '',
          style_name: '',
          style_len: 0,
          pool_type: 'all',
          gender: 'all',
          selected_name: 'all',
          training_table: { ...filters.training_table, mode: 'bySession' },
        },
      }));
    };

    const loadCompetitions = async (title: string) => {
      const all: any[] = [];
      let page = 1;
      for (;;) {
        const r = await fetch(
          `/api/hub-groups/${encodeURIComponent(groupSlug)}/results?page=${page}&pageSize=500`,
          { credentials: 'same-origin' },
        );
        if (cancelled) return;
        if (r.status === 404) { setGroupError('Group not found.'); return; }
        if (!r.ok) { setGroupError(`Failed to load results (${r.status}).`); return; }
        const json = await r.json();
        all.push(...(json.data ?? []));
        if (!json.hasMore) break;
        page += 1;
      }
      setGroupError(null);
      dispatch(rootActions.updateState({
        dataSourceSelected: {
          results: all,
          title,
          is_masters: all.some((r) => r.is_masters === true),
          is_award: all.some((r) => r.is_award === true),
          sourceParams: { group: groupSlug },
        },
        filterSelected: { ...filters, activity_type: 'competition' },
      }));
    };

    (async () => {
      try {
        // Полный DTO группы — для богатой шапки (иконка, official-бейдж, links, участники)
        let title = groupSlug;
        const info = await fetch(`/api/hub-groups/${encodeURIComponent(groupSlug)}`, { credentials: 'same-origin' });
        if (!cancelled && info.ok) {
          const dto: HubGroupDetails = await info.json();
          setGroupInfo(dto);
          title = dto.name_en || dto.name || groupSlug;
        }
        await (groupTab === 'trainings' ? loadTrainings() : loadCompetitions(title));
      } catch {
        if (!cancelled) setGroupError(`Failed to load ${groupTab}.`);
      }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupSlug, groupTab]);

  const trainingGroupError = groupError;

  // Сброс selected_name при закрытии мобильного модала
  const handleMobileModalClose = useCallback(() => {
    dispatch(rootActions.updateState({
      filterSelected: { ...filters, selected_name: 'all' },
    }));
  }, [dispatch, filters]);

  // === Проверка, выбран ли источник ===
  // По title, а не по длине results: в paged-режиме первая страница с дефолтными
  // фильтрами может законно вернуть 0 строк — форма фильтров всё равно должна остаться видимой.
  const hasSource = !!selectedSource?.title;

  const filtersSummary = useMemo(
    () => summarizeFilters(filters, isTraining),
    [filters, isTraining]
  );

  return (
    <div className="dolphine-training md:p-4 pt-safe pb-safe min-h-screen bg-[var(--theme-mode-page-bg)]">
      {/* Топбар full-bleed: гасим паддинги корня, чтобы полоса шла от края до края
          и шапка селектора/группы прижималась к ней слитным блоком (без зазора).
          sticky — на обёртке: внутри неё sticky самого топбара некуда «липнуть». */}
      <div className="sticky top-0 z-50 md:-m-4 md:mb-0">
        <AppTopbar />
      </div>
      {/* Переключатель Light/Dark (fixed внизу-справа) */}
      <UI_ModeToggle />
      {/* Dev-инструмент тем (виден только при ?themes в URL) */}
      <UI_ThemeDevTool />

      {/* Хаб группы (?group=): богатая шапка из HubGroupDetails + табы.
          Overview/Members/Records — ссылки на обзорную groups.html; Competitions/Trainings —
          локальный toggle (единственное место переключения активности, см. FilterActivity). */}
      {groupSlug ? (
        <div className="relative w-full z-40 max-md:px-2 max-md:pt-2 md:-mx-4 md:w-auto">
          <GroupHeader
            slug={groupSlug}
            group={groupInfo}
            highlights={groupInfo?.highlights}
            activeTab={groupTab}
            onTabChange={setGroupTab}
          />
        </div>
      ) : (
        /* Селектор соревнований: шапка/панель, единый для всех брейкпоинтов
            (mobile — bottom sheet из «Change», design_handoff_selector_all) */
        <div className="relative w-full z-40 max-md:px-2 max-md:pt-2 md:-mx-4 md:w-auto">
          <DataSourceDDL />
        </div>
      )}

      {/* Источник не выбран (deep-link ?category=): селектор выше открыт inline,
          область контента приглушена с подсказкой (CHANGES.md, frame 7a) */}
      {trainingGroupError ? (
        <div
          className="mt-4 flex min-h-[140px] items-center justify-center rounded-[14px] px-4 text-center text-[13px] font-semibold"
          style={{
            border: '1px dashed var(--theme-mode-border-input)',
            background: 'var(--theme-mode-surface)',
            color: 'var(--theme-mode-text-muted)',
          }}
        >
          {trainingGroupError}
        </div>
      ) : !hasSource ? (
        <div
          className="mt-4 flex min-h-[140px] items-center justify-center rounded-[14px] text-[13px] font-semibold opacity-45"
          style={{
            border: '1px dashed var(--theme-mode-border-input)',
            background: 'var(--theme-mode-surface)',
            color: 'var(--theme-mode-text-muted)',
            pointerEvents: 'none',
          }}
        >
          Select a competition above to see results
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

            {/* Центральная колонка — результаты (детали теперь попапом, поэтому полная ширина) */}
            {!isTraining && (
              <div className="results-table w-full lg:w-10/12 bg-[var(--theme-mode-surface)] text-[var(--theme-mode-text)] rounded shadow">
                <ResultsTable />
              </div>
            )}
            {isTraining && (
              <div className="w-full lg:w-10/12 bg-[var(--theme-mode-surface)] text-[var(--theme-mode-text)] p-0 md:p-4 rounded shadow">
                <TrainingTable />
              </div>
            )}

            {/* POPUP */}
            {isPopup && <Popup />}
          </div>

          {/* Мобильная нижняя шторка с фильтрами (во весь экран при раскрытии) */}
          <MobileFiltersDrawer summary={filtersSummary}>
            {!isTraining ? <FilterSection /> : <FilterTrainigSection />}
          </MobileFiltersDrawer>

          {/* Мобильный модал с деталями спортсмена (показывается вместо правой колонки на моб/планшет) */}
          <MobileSportsmenModal
            selectedName={filters?.selected_name && filters.selected_name !== 'all' ? filters.selected_name : null}
            onClose={handleMobileModalClose}
          >
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
    onClose,
  }: {
    selectedName?: string | null;
    children: React.ReactNode;
    onClose?: () => void;
  }) {
    const [open, setOpen] = useState(false);

    // открываем автоматом, когда выбранное имя меняется на truthy; закрываем если имя снято
    React.useEffect(() => {
      if (selectedName) setOpen(true);
      else setOpen(false);
    }, [selectedName]);

    const handleClose = () => {
      setOpen(false);
      onClose?.();
    };

    const node = (
      <>
        {/* Оверлей: сама карточка деталей И ЕСТЬ модал (без белого враппера вокруг).
            Тёмная подложка + скролл, карточка отрисовывает всё оформление сама. */}
        {open &&
          createPortal(
            <div
              className="fixed inset-0 z-[120] flex items-start justify-center overflow-y-auto p-4"
              style={{
                background: 'var(--theme-mode-overlay)',
                backdropFilter: 'blur(3px)',
                WebkitBackdropFilter: 'blur(3px)',
              }}
              onClick={handleClose}
            >
              <div
                className="relative w-full max-w-[460px]"
                onClick={(e) => e.stopPropagation()}
              >
                {/* Кнопка закрытия — над карточкой, на тёмной подложке */}
                <div className="flex justify-end mb-2">
                  <button
                    onClick={handleClose}
                    className="w-8 h-8 rounded-full bg-white/90 text-gray-700 shadow flex items-center justify-center text-xl leading-none hover:bg-white"
                    aria-label="Close"
                  >
                    ×
                  </button>
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
