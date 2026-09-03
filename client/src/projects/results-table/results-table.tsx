import React, { useEffect, useMemo, useState } from 'react';
import { ROUND_ORDER, roundSectionTitle } from '../components/mix/round-label/round-label';
import './results-table.css';
import { rootActions,useAppDispatch, useAppSelector } from '../../store/store';
import { useFavoritesContext } from '../../hooks/favorites-context';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import Helper from '../../utils/helpers/data-helper'
import HelperResults from '../../utils/helpers/helper-results'
import { loadRecordAgeAxis } from '../../utils/helpers/record-age-axis'
import ClubPointsHelper from '../../utils/helpers/club-points-helper';
import UI_DateIcon from '../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../components/mix/pool-icon/pool-icon'
import ResultsTableMobile from './components/results-table-mobile';
import ResultsTableDesktop from './components/results-table-desktop';
import ResultsHeader from './components/results-header';
import ResultsFilteredInfo from './components/results-filtered-info';
import { TOP_N_POSITIONS } from '../../utils/constants/filter-constants';
import HelperSwimmer from '../../utils/helpers/helper-swimmer';
import { applyCombinedPositions } from '../../utils/helpers/recalculate-positions';
import NormativeAgeRecords from './components/normative-age-records';
import NormativeMastersRecords from './components/normative-masters-records';
import '../components/mix/text-effect/text-effect.css';
import { useResultsLoadMode } from '../../hooks/useResultsLoadMode';
import { useCompetitionMedia } from '../../hooks/useCompetitionMedia';
import { buildResultsFilterParams, fetchResultsPage } from '../../utils/helpers/results-api';
import AddLinkModal from '../my-media-project/components/add-link-modal';
import { swimFlaggedRowProps } from '../components/mix/swim-time/swim-time';
import { parseDate } from '../../utils/helpers/competition-source';
import { seasonStartYear, ageInSeason } from '../../utils/helpers/season-helper';
import SeasonBestTable, { timeToMs } from '../../utils/helpers/season-best-table';
import { useSeasonBestTable } from '../../hooks/useSeasonBestTable';
import { addUserMedia } from '../my-media-project/use-all-my-media';

function ResultsTable() {
  const dispatch = useAppDispatch();
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const filters = useAppSelector((state) => state.filterSelected);
  const isMastersSource = !!selectedSource?.is_masters;
  const isAwardSource = !!selectedSource?.is_award;
  const loadMode = useResultsLoadMode();
  const resultsPaging = useAppSelector((state) => state.resultsPaging);
  const [loadingMore, setLoadingMore] = useState(false);

  const {
    isAuthenticated,
    favoriteSwimmerIds,
    primarySwimmerId,
    toggleFavoriteSwimmer,
    togglePrimarySwimmer,
  } = useFavoritesContext();
  const { openLoginModal } = useLoginModal();

  // Медиа заплывов, видимое зрителю (своё + одобренные публикации) — иконки видео на строках.
  const { byResultId: mediaByResultId, refresh: refreshMedia } = useCompetitionMedia(selectedSource?.sourceParams);

  // Режим «добавить видео» (тумблер в шапке) — только локальный стейт, не persist, только залогиненным.
  const [addVideoMode, setAddVideoMode] = useState(false);
  // Заплыв, для которого сейчас открыт попап AddLinkModal (single-step)
  const [addVideoFor, setAddVideoFor] = useState<{
    resultId: number;
    swimmerId: number;
    contextLabel: React.ReactNode;
  } | null>(null);

  // По title, а не по длине results: в paged-режиме первая страница с текущими фильтрами
  // может законно вернуть 0 строк — это "No results match", а не "источник не выбран".
  if (!selectedSource || !selectedSource.title) {
    return <div className="text-[var(--theme-mode-text-muted)] italic">No data source selected.</div>;
  }

  // Пересчёт позиций (если включён is_recalculated) — ДО фильтрации
  const isRecalculated = !!filters.is_recalculated;
  const sourceResults = useMemo(() => {
    const raw = selectedSource.results ?? [];
    if (!isRecalculated) return raw;
    // Места объединённого зачёта считает сервер (combined_place); локальный пересчёт —
    // фоллбек для статических источников. position подменяется, чтобы фильтры и сортировка
    // работали с выбранным местом; оригинал остаётся в position_original.
    return applyCombinedPositions(raw);
  }, [selectedSource, isRecalculated]);

  // Базовая фильтрация (все фильтры, КРОМЕ level_filter)
  const baseFilteredResults = useMemo(() => sourceResults.filter((res) => {
    const { pool_type, gender, style_name, style_len, date, age, club, activity_type, position_filter, event_date } = filters;
    const resPoolType = Helper.resolvePoolType(res.pool_type);
    const filterPoolType = pool_type === 'all' ? null : Helper.resolvePoolType(pool_type);
    
    // Фильтр по дате события
    if (event_date && event_date !== 'all' && res.date !== event_date) return false;

    // Предварительные заплывы по умолчанию скрыты (официальный вид — финалы);
    // тумблер [prelim] в фильтре Date возвращает их. Строки без признака (timed final,
    // старые данные) видны всегда.
    if (HelperResults.isHiddenHeat(res.heat_type) && !filters.show_prelims) return false;

    // Фильтр по категории (программе) заплыва. У строк, импортированных до появления
    // event_category, оно пустое — такие в выборку по конкретной категории не попадают.
    const catFilter = filters.event_category;
    if (catFilter && catFilter !== 'all' && (res.event_category ?? '') !== catFilter) return false;
    
    // Фильтр по training/competition
    const hasTraining = !!res.training?.trainingId;
    const activityType = activity_type || 'training';
    if (activityType === 'training' && !hasTraining) return false;
    if (activityType === 'competition' && hasTraining) return false;

    // Диплинк на заплывы конкретного пловца (?swimmerId=, routes.competitionSwims).
    // Явный пловец, поэтому применяется и гостю — в отличие от скоупа ниже.
    if (filters.swimmer_id != null && !HelperSwimmer.resultBelongsToSwimmer(res, filters.swimmer_id)) {
      return false;
    }

    // Скоуп «мои/избранные» (персональная полоса шапки соревнования, ?filter=).
    // Гостю или без primary — скоуп молча не применяется (пустая таблица хуже).
    // Матчинг — ТОЛЬКО через HelperSwimmer.resultBelongsTo* (эстафеты по составу ног).
    const scope = filters.swimmer_scope;
    if (scope === 'my' && isAuthenticated && primarySwimmerId != null) {
      if (!HelperSwimmer.resultBelongsToSwimmer(res, primarySwimmerId)) return false;
    } else if (scope === 'favorites' && isAuthenticated && favoriteSwimmerIds.size > 0) {
      if (!HelperSwimmer.resultBelongsToAny(res, favoriteSwimmerIds)) return false;
    }

    // Фильтр по позиции (месту)
    const posFilter = position_filter || 'top';
    if (posFilter === 'podium') {
      const pos = Number(res.position);
      if (!pos || pos > 3) return false;
    } else if (posFilter === 'top') {
      const pos = Number(res.position);
      if (pos && pos > TOP_N_POSITIONS) return false;
    }
    
    return (
      (!filterPoolType || resPoolType === filterPoolType) &&
      (gender === 'all' || res.event_style_gender === gender) &&
      (!style_name || res.event_style_name === style_name) &&
      (!style_len || res.event_style_len === style_len.toString()) &&
      (age === 'all' || (
        /^\d+-\d+$/.test(age)
          ? res.age_group === age
          : filters.age_to
            ? Number(res.birth_year) >= Number(age) && Number(res.birth_year) <= Number(filters.age_to)
            : res.birth_year?.toString() === age
      )) &&
      (club === 'all' || res.club === club)
    );
  }), [sourceResults, filters, isAuthenticated, primarySwimmerId, favoriteSwimmerIds]);

  // Вычисляем наивысший уровень из базовых результатов и пушим в store
  // Ось возраста для сверки с рекордами приезжает из /api/client-config: без неё
  // бейдж строки считался бы по своей оси и разошёлся с карточкой «New records».
  const [, setAxisLoaded] = React.useState(0);
  React.useEffect(() => {
    let alive = true;
    loadRecordAgeAxis().then(() => { if (alive) setAxisLoaded((n) => n + 1); });
    return () => { alive = false; };
  }, []);

  useEffect(() => {
    let highestPriority = 0;
    let bestInfo: import('../../store/store').BestLevelInfo | null = null;

    for (const res of baseFilteredResults) {
      const isMaster = Helper.isResultMasters(isMastersSource, res.event_style_age);
      const resolvedGender = Helper.resolveGender(res.event_style_gender);
      const levelInfo = Helper.getNormativeLevelInfo({
        gender: resolvedGender === 'none' ? 'male' : resolvedGender,
        poolType: Helper.resolvePoolType(res.pool_type),
        styleName: res.event_style_name,
        distance: `${res.event_style_len}m`,
        time: Helper.parseTimeToSeconds(res.time),
        isMaster,
        event_style_age: res.event_style_age,
      });
      const lvl = levelInfo?.currentLevel;
      if (!lvl || lvl === '—' || lvl === '-') continue;
      const priority = HelperSwimmer.levelPriority[lvl] ?? 0;
      if (priority > highestPriority) {
        highestPriority = priority;
        bestInfo = {
          levelName: lvl,
          styleName: res.event_style_name,
          styleLen: res.event_style_len,
          poolType: res.pool_type,
          isMasters: isMaster,
        };
      }
    }

    dispatch(rootActions.updateState({ bestLevelInfo: bestInfo }));
  }, [baseFilteredResults]);

  // Применяем level_filter поверх базовых результатов
  const filteredResults = useMemo(() => {
    const level_filter = filters.level_filter;
    if (!level_filter || level_filter === 'all') return baseFilteredResults;

    return baseFilteredResults.filter((res) => {
      const isMaster = Helper.isResultMasters(isMastersSource, res.event_style_age);
      const resolvedGender = Helper.resolveGender(res.event_style_gender);
      const levelInfo = Helper.getNormativeLevelInfo({
        gender: resolvedGender === 'none' ? 'male' : resolvedGender,
        poolType: Helper.resolvePoolType(res.pool_type),
        styleName: res.event_style_name,
        distance: `${res.event_style_len}m`,
        time: Helper.parseTimeToSeconds(res.time),
        isMaster,
        event_style_age: res.event_style_age,
      });
      const lvl = levelInfo?.currentLevel;
      if (!lvl || lvl === '—' || lvl === '-') return false;
      const resPriority = HelperSwimmer.levelPriority[lvl] ?? 0;
      const filterPriority = HelperSwimmer.levelPriority[level_filter] ?? 0;
      return resPriority >= filterPriority;
    });
  }, [baseFilteredResults, filters.level_filter]);

  //console.log('filteredResults: ',filteredResults)
  const sortedResults = useMemo(
    () => Helper.sortByTime(filteredResults),
    [filteredResults],
  );

  // displayResults = sortedResults (recalculation уже применён в sourceResults)
  const displayResults = sortedResults;

  // Раунды зачёта: у чемпионата «мокдамот и финал» утренние заплывы по возрастным группам
  // и вечерний финал — ДВА САМОСТОЯТЕЛЬНЫХ зачёта со своей нумерацией мест. В одном списке
  // по времени они дают «1, 1, 2, 2» и читаются как задвоение, поэтому раунды разведены
  // по ТАБАМ: в каждом — сплошная таблица одного зачёта. Обычные соревнования (раунда нет)
  // не видят ни табов, ни изменений: набор раундов там из одного значения.
  const roundOf = (r: { round?: string | null }) => r.round ?? '';
  const roundTabs = useMemo(() => {
    const present = Array.from(new Set(displayResults.map(roundOf)));
    return present.length > 1
      ? present.sort((a, b) => (ROUND_ORDER[a] ?? 99) - (ROUND_ORDER[b] ?? 99))
      : [];
  }, [displayResults]);

  const [activeRound, setActiveRound] = useState<string | null>(null);
  // Набор раундов меняется вместе с фильтрами (напр. включили предварительные) — выбор
  // сбрасываем на первый доступный, иначе таблица молча опустеет.
  useEffect(() => {
    if (roundTabs.length === 0) { if (activeRound !== null) setActiveRound(null); return; }
    if (activeRound === null || !roundTabs.includes(activeRound)) setActiveRound(roundTabs[0]);
  }, [roundTabs, activeRound]);

  /** Подпись таба: раунду — его название, строкам без раунда — что они такое. */
  const roundTabLabel = (round: string) => roundSectionTitle(round)
    ?? (displayResults.filter((r) => roundOf(r) === round).every((r) => r.is_relay) ? 'Relays' : 'Other swims');

  const orderedResults = useMemo(
    () => (roundTabs.length > 0 && activeRound !== null
      ? displayResults.filter((r) => roundOf(r) === activeRound)
      : displayResults),
    [displayResults, roundTabs, activeRound],
  );

  // Диплинк на заплыв (?swim=<resultId>, НАВ-контракт шапки соревнования):
  // после появления строк скроллим к строке и подсвечиваем её на несколько секунд.
  const highlightedSwimRef = React.useRef<string | null>(null);
  useEffect(() => {
    const swim = new URLSearchParams(window.location.search).get('swim');
    if (!swim || highlightedSwimRef.current === swim || displayResults.length === 0) return;
    // Таймер НЕ отменяем в cleanup: смена ссылки displayResults на быстрых
    // ре-рендерах после переключения таба гасила бы его до срабатывания;
    // повторные срабатывания гейтит highlightedSwimRef.
    setTimeout(() => {
      if (highlightedSwimRef.current === swim) return;
      const els = Array.from(document.querySelectorAll(`[data-result-id="${CSS.escape(swim)}"]`));
      const el = els.find((e) => (e as HTMLElement).offsetParent !== null) as HTMLElement | undefined;
      if (!el) return;
      highlightedSwimRef.current = swim;
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      el.classList.add('swim-deeplink-highlight');
      setTimeout(() => el.classList.remove('swim-deeplink-highlight'), 4000);
    }, 300);
  }, [displayResults]);

  const getResultKey = (res: any) =>
    [
      res.date,
      res.competition,
      res.event,
      res.event_style_name,
      res.event_style_len,
      res.pool_type,
      res.first_name,
      res.last_name,
      res.time,
      String(res.position ?? ''),
      res.club,
      res.relay_team_name,
      String(res.is_relay ?? ''),
    ]
      .map((v) => (v === null || v === undefined ? '' : String(v)))
      .join('||');

  // Мобильный вид: раскрытие нижней строки (level/pts/date). Дефолт — все закрыты.
  const [showAllOpen, setShowAllOpen] = useState(true);
  const [expandedOverrides, setExpandedOverrides] = useState<Record<string, boolean>>({});

  const handleToggleShowAllOpen = () => {
    setShowAllOpen((prev) => !prev);
    setExpandedOverrides({});
  };

  const handleToggleRowExpand = (key: string, currentlyExpanded: boolean) => {
    setExpandedOverrides((prev) => ({ ...prev, [key]: !currentlyExpanded }));
  };

  // Очки клуба (Э6): считает сервер и кладёт в club_points / combined_club_points —
  // клиент только выбирает поле по тоггл'у «Combine All Results». Локальный расчёт
  // остаётся ФОЛЛБЕКОМ для старых статических источников, где полей ещё нет: он не знает
  // привязку соревнования к правилу и на manual-правилах расходится с зачётом.
  const [clubPointsByKey, setClubPointsByKey] = useState<Record<string, number>>({});

  useEffect(() => {
    let cancelled = false;

    const loadClubPoints = async () => {
      const legacyRows = sortedResults.filter((r) => r.club_points === undefined);
      if (legacyRows.length === 0) {
        if (!cancelled) setClubPointsByKey({});
        return;
      }

      const pointsMap = await ClubPointsHelper.getPointsMapForResults(legacyRows, getResultKey);
      if (cancelled) return;
      setClubPointsByKey(pointsMap);
    };

    loadClubPoints();

    return () => {
      cancelled = true;
    };
  }, [sortedResults]);

  /** Очки строки: серверные (с учётом тоггла) → фоллбек на локальный расчёт. */
  const clubPointsOf = (res: any): number | undefined => {
    if (res.club_points === undefined) return clubPointsByKey[getResultKey(res)];
    return isRecalculated && res.combined_club_points != null
      ? res.combined_club_points
      : res.club_points;
  };

  // Определяем уникальные значения
  const uniqueClubs = new Set(displayResults.map(r => r.club));
  const uniqueStyleName = new Set(displayResults.map(r => `${r.event_style_name}-${r.event_style_len}`));
  const uniqueDates = new Set(displayResults.map(r => r.date));
  const uniqueAge = new Set(displayResults.map(r => r.event_style_age));
  const uniquePoolType = new Set(displayResults.map(r => Helper.resolvePoolType(r.pool_type)));

  const showClub = uniqueClubs.size > 1;
  const showEvent = uniqueStyleName.size > 1;
  const showDate = uniqueDates.size > 1;
  // Здесь всегда одно соревнование (мультидневные — те же дни одного турнира),
  // название рядом с датой не нужно — только в all-time карточке спортсмена.
  const showCompetition = false;
  const showAge = uniqueAge.size > 1;
  const showPoolType = uniquePoolType.size > 1;

  const hasInternationalPoints = displayResults.some(r => 
  r.international_points !== undefined &&
  r.international_points !== null &&
  !isNaN(Number(r.international_points))
);

  const firstResult = displayResults[0];
  // Сезон открытого протокола — для таба «Season best» в карточке рекордов: страница чаще
  // всего про прошедший старт, а не про текущий сезон (season-boundary-rule).
  const competitionDate = firstResult?.date ? parseDate(firstResult.date) : null;
  const competitionSeason = competitionDate ? seasonStartYear(competitionDate) : null;
  const poolTypeDisplay = showPoolType ? 'all' : (firstResult?.pool_type ?? filters.pool_type);
  // Эталон для бейджа SB: вся сезонная таблица одним запросом (как справочник рекордов).
  // Сезон — тот же, что у карточки Season best, то есть сезон открытого протокола.
  const seasonBestReady = useSeasonBestTable(competitionSeason);
// Функция обновления фильтров
  const updateFilter = (newFilter: Partial<typeof filters>) => {
      dispatch(rootActions.updateState({ filterSelected: { ...filters, ...newFilter } }));
  };

  // Paged-режим: «Show more» догружает следующую страницу и аппендит к уже загруженным (контракт 3.2 §3).
  const handleShowMore = async () => {
    const sourceParams = selectedSource.sourceParams;
    if (!resultsPaging || !sourceParams || loadingMore) return;
    setLoadingMore(true);
    try {
      const filterParams = buildResultsFilterParams(filters, isMastersSource);
      const nextPage = resultsPaging.page + 1;
      const { results, paging } = await fetchResultsPage(
        sourceParams, filterParams, nextPage, resultsPaging.pageSize,
      );
      dispatch(
        rootActions.updateState({
          dataSourceSelected: {
            ...selectedSource,
            results: [...(selectedSource.results ?? []), ...results],
          },
          resultsPaging: paging,
        }),
      );
    } catch (e) {
      console.error('Error loading more results', e);
    } finally {
      setLoadingMore(false);
    }
  };

  return (
    <div className="results table w-full">
      <div className="mb-4">

        {/* Зелёный баннер соревнования удалён: его заменила шапка селектора
            (компактный header с кнопкой «Change», см. design_handoff_category_selector/CHANGES.md) */}
        <ResultsFilteredInfo
          firstResult={firstResult}
          showDate={showDate}
          showClub={showClub}
          showAge={showAge}
          showPoolType={showPoolType}
          showEvent={showEvent}
        />      
        {/* Бассейн берём фактический (poolTypeDisplay, как у masters-карточки ниже), а не
            сырой filters.pool_type: по умолчанию он 'all', а при 'all' карточка отдавала
            первый попавшийся ключ — 25m — и на 50-метровом соревновании показывала рекорды
            короткого бассейна. */}
        {!isMastersSource && (
          <NormativeAgeRecords
            gender={filters.gender}
            poolType={poolTypeDisplay}
            styleName={filters.style_name}
            styleLen={filters.style_len}
            age={filters.age}
            season={competitionSeason}
          />
        )}
        {isMastersSource && (
          <NormativeMastersRecords
            gender={filters.gender}
            poolType={poolTypeDisplay}
            styleName={filters.style_name}
            styleLen={filters.style_len}
            age={filters.age}
          />
        )}
        
        {roundTabs.length > 0 && (
          <div className="mb-2 flex flex-wrap items-center gap-2">
            {roundTabs.map((round) => (
              <button
                key={round}
                className={`fseg ${activeRound === round ? 'fseg-active' : ''}`}
                onClick={() => setActiveRound(round)}
              >
                {roundTabLabel(round)}
                <span className="ml-1 opacity-60">
                  {displayResults.filter((r) => roundOf(r) === round).length}
                </span>
              </button>
            ))}
          </div>
        )}

        <div className="max-h-[650px] overflow-y-auto border border-[var(--theme-mode-border)] rounded-2xl shadow-sm" >
          {/* Unified header (single view for all sizes) */}
          <div className="bg-[var(--theme-mode-surface-alt)] sticky top-0 z-10">
            <div className="hidden lg:block">
              <ResultsHeader
                view="desktop"
                showClub={showClub}
                showEvent={showEvent}
                showPoolType={showPoolType}
                showDate={showDate}
                hasInternationalPoints={hasInternationalPoints}
                addVideoMode={addVideoMode}
                onToggleAddVideoMode={isAuthenticated ? () => setAddVideoMode((v) => !v) : undefined}
              />
            </div>
            <div className="lg:hidden">
              <ResultsHeader
                view="mobile"
                showClub={showClub}
                showEvent={showEvent}
                showPoolType={showPoolType}
                showDate={showDate}
                hasInternationalPoints={hasInternationalPoints}
                showAllOpen={showAllOpen}
                onToggleShowAllOpen={handleToggleShowAllOpen}
                addVideoMode={addVideoMode}
                onToggleAddVideoMode={isAuthenticated ? () => setAddVideoMode((v) => !v) : undefined}
              />
            </div>
          </div>
          <ul>
            {orderedResults.map((res, index) => {
              const clubPoints = clubPointsOf(res);
              const isMaster = Helper.isResultMasters(isMastersSource, res.event_style_age);
              const resolvedGender = Helper.resolveGender(res.event_style_gender);
              // Пол для мягкой заливки строки — из названия события (поля gender в данных нет)
              const rowGender = Helper.resolveGenderFromEvent(res.event_name || res.event, res.event_style_gender);
              const swimmerName = `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}`;
              // Пол для рекордов/нормативов: явное поле → из названия события → 'male'
              const genderForRecord = resolvedGender !== 'none' ? resolvedGender : (rowGender !== 'none' ? rowGender : 'male');
              const recordParams = {
                gender: genderForRecord,
                poolType: res.pool_type,
                styleName: res.event_style_name,
                distance: `${res.event_style_len}m`,
                // Ключ поиска в справочнике — по оси RecordAgeAxis, а не возраст пловца:
                // иначе бейдж строки разойдётся с карточкой «New records» (её считает сервер).
                age: Helper.recordStepAge(res),
                isMasters: isMaster,
              };
              // Заплыв, помеченный админом как ошибка протокола, НЕ носит бейдж рекорда:
              // иначе бессмыслица (200 вольным за 1:53 у 13-летнего) выглядит достижением.
              // Строку при этом не прячем — протокол напечатан так, как напечатан.
              const isSuspect = !!res.suspect_reason;
              // Класс рекорда ищется по ЗАПЛЫВУ, от старшего к младшему (national > age >
              // masters): у взрослого эталон — открытый рекорд страны, у мастерского старта
              // полоса возраста, у ребёнка его ступень. Раньше класс задавался догадкой
              // «masters или age», и держатель национального рекорда выглядел в протоколе
              // обычным первым номером (поймано 02.09.2026 на 00:56.73 Горбенко).
              const recordHolderMark = isSuspect
                ? null
                : Helper.recordMarkForHolder({ swimmerName, ...recordParams });
              const recordTimeMark = isSuspect
                ? null
                : Helper.recordMarkForTime({ time: res.time, ...recordParams });
              const isRecordHolder = recordHolderMark != null;
              const isRecordTime = recordTimeMark != null;
              // Бейдж SB — «лучшее время страны в этом сезоне на своей ступени». Проверка
              // локальная по сезонной таблице (та же схема, что у рекорда выше), но ось
              // возраста тут СЕЗОННАЯ, а не календарная ось справочника: `ageInSeason`, а
              // не `recordStepAge` (client/CLAUDE.md, footguns).
              //
              // Что пропускаем молча: помеченные ошибки протокола, эстафеты, мастерские
              // старты и строки не из сезона таблицы — ровно тот состав, который сервер
              // исключил при её расчёте (`GetSeasonBestTableAsync`). Пометить их значило бы
              // сравнить с эталоном, в который они не входили.
              const rowDate = res.date ? parseDate(res.date) : null;
              // Сезон СТРОКИ, а не источника: в сборной выдаче (несколько соревнований)
              // строка из другого сезона мерялась бы чужим эталоном. Не совпало — бейджа нет.
              const rowSeason = rowDate ? seasonStartYear(rowDate) : null;
              const seasonBestAge = seasonBestReady && !isSuspect && !res.is_relay && !isMaster
                && rowSeason != null && rowSeason === competitionSeason && rowDate
                ? ageInSeason(res.birth_year, rowDate)
                : null;
              const isSeasonBestTime = seasonBestAge != null && competitionSeason != null
                && SeasonBestTable.isSeasonBest(
                  competitionSeason,
                  {
                    styleName: res.event_style_name,
                    distance: res.event_style_len,
                    poolType: res.pool_type,
                    gender: genderForRecord,
                    age: seasonBestAge,
                  },
                  timeToMs(res.time),
                );
              const levelInfo = Helper.getNormativeLevelInfo({
                gender: genderForRecord,
                poolType: Helper.resolvePoolType(res.pool_type),
                styleName: res.event_style_name,
                distance: `${res.event_style_len}m`,
                time: Helper.parseTimeToSeconds(res.time),
                isMaster,
                event_style_age: res.event_style_age,
              });

              const swimmerId = res.swimmer_id;
              const isFav = isAuthenticated && swimmerId != null && favoriteSwimmerIds.has(swimmerId);
              const isPrimary = isAuthenticated && swimmerId != null && swimmerId === primarySwimmerId;
              // Гость (фаза 4.5): сердечко рендерим в "пустом" состоянии, клик открывает
              // LoginModal вместо undefined-колбэка — фича видна, но требует логина.
              // "это я"/primary гостю не нужен, там просто ничего не показываем.
              const favoriteProps = isAuthenticated
                ? {
                    // "звезда отменяет сердечко": у primary (me) сердечко в таблице не горит
                    isFavorite: isFav && !isPrimary,
                    isPrimaryFavorite: isPrimary,
                    onToggleFavorite: swimmerId != null && !res.is_relay
                      ? () => toggleFavoriteSwimmer(swimmerId)
                      : undefined,
                    onTogglePrimary: isFav && swimmerId != null && !res.is_relay
                      ? () => togglePrimarySwimmer(swimmerId)
                      : undefined,
                  }
                : {
                    isFavorite: false,
                    isPrimaryFavorite: false,
                    onToggleFavorite: swimmerId != null && !res.is_relay
                      ? () => openLoginModal()
                      : undefined,
                    onTogglePrimary: undefined,
                  };

              const resultKey = getResultKey(res);
              const isRowExpanded = expandedOverrides[resultKey] ?? showAllOpen;

              // Медиа заплыва (видимое зрителю) подмешиваем в gallery — иконку/лайтбокс
              // рендерит уже существующий UI_SwimmerGallery внутри строк.
              const rowMedia = typeof (res as any).id === 'number' ? mediaByResultId.get((res as any).id) : undefined;
              const resForRow = rowMedia?.length
                ? { ...res, gallery: [...(res.gallery ?? []), ...rowMedia] }
                : res;

              // Иконка «добавить видео» — только режим включён + числовой result id + не эстафета (п.4 таска)
              const resultIdForVideo = typeof (res as any).id === 'number' ? (res as any).id : null;
              const canAddVideo = addVideoMode && swimmerId != null && resultIdForVideo != null && !res.is_relay;
              const onAddVideo = canAddVideo
                ? () => {
                    const swimmerLabel = `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}`;
                    setAddVideoFor({
                      resultId: resultIdForVideo!,
                      swimmerId: swimmerId!,
                      contextLabel: (
                        <div className="flex flex-col gap-0.5">
                          <span className="font-bold">{swimmerLabel}</span>
                          <span>{res.event_style_len}m {res.event_style_name} · {res.time}</span>
                          <span className="opacity-70">{res.competition} · {res.date}</span>
                        </div>
                      ),
                    });
                  }
                : undefined;

              // Спорное время (гибрид 15d): caution-лента слева во всю высоту строки + чип
              // «⚠ Under review» под временем (рисует UI_SwimTime). Признак тот же самый,
              // по которому раньше стоял голый значок ⚠ — suspect_reason с сервера.
              const isFlagged = !!resForRow.suspect_reason;
              const flagProps = swimFlaggedRowProps(
                isFlagged ? { kind: 'protocol', reason: resForRow.suspect_reason } : null);

              return (
                <React.Fragment key={index}>
                  <li
                    data-result-id={resForRow.id ?? undefined}
                    {...flagProps}
                    className={`lg:hidden flex flex-col gap-2 ${isFlagged ? 'pl-5 pr-3' : 'px-3'} py-2 rounded border-l-4 border-b border-b-[var(--theme-mode-border-row)] ${flagProps.className ?? ''} ${isPrimary ? 'border-l-[#f5b800] bg-[var(--theme-mode-me-highlight)]' : `border-l-transparent ${Helper.getGenderBgClass(rowGender)}`}`}
                  >
                    <ResultsTableMobile
                      res={resForRow}
                      index={index}
                      showAge={showAge}
                      showClub={showClub}
                      showEvent={showEvent}
                      showPoolType={showPoolType}
                      showDate={showDate}
                      showCompetition={showCompetition}
                      hasInternationalPoints={hasInternationalPoints}
                      clubPoints={clubPoints}
                      levelInfo={levelInfo}
                      updateFilter={updateFilter}
                      isMastersResult={isMaster}
                      isAwardSource={isAwardSource}
                      isRecordHolder={isRecordHolder}
                      isRecordTime={isRecordTime}
                      recordHolderMark={recordHolderMark}
                      recordTimeMark={recordTimeMark}
                      isSeasonBestTime={isSeasonBestTime}
                      isExpanded={isRowExpanded}
                      onToggleExpand={() => handleToggleRowExpand(resultKey, isRowExpanded)}
                      onAddVideo={onAddVideo}
                      {...favoriteProps}
                    />
                  </li>

                  <li
                    data-result-id={resForRow.id ?? undefined}
                    {...flagProps}
                    className={`hidden lg:grid ${flagProps.className ?? ''} ${isFlagged ? 'pl-2' : ''} ${isPrimary ? '' : Helper.getGenderBgClass(rowGender)}`}
                  >
                    <ResultsTableDesktop
                      res={resForRow}
                      index={index}
                      showAge={showAge}
                      showClub={showClub}
                      showEvent={showEvent}
                      showPoolType={showPoolType}
                      showDate={showDate}
                      showCompetition={showCompetition}
                      hasInternationalPoints={hasInternationalPoints}
                      clubPoints={clubPoints}
                      levelInfo={levelInfo}
                      updateFilter={updateFilter}
                      isMastersResult={isMaster}
                      isAwardSource={isAwardSource}
                      isRecordHolder={isRecordHolder}
                      isRecordTime={isRecordTime}
                      recordHolderMark={recordHolderMark}
                      recordTimeMark={recordTimeMark}
                      isSeasonBestTime={isSeasonBestTime}
                      onAddVideo={onAddVideo}
                      {...favoriteProps}
                    />
                  </li>

                </React.Fragment>
              );
            })}

            {displayResults.length === 0 && (
              <li className="text-center text-[var(--theme-mode-text-muted)] py-4">No results match the current filters.</li>
            )}
          </ul>
        </div>

        {loadMode === 'paged' && resultsPaging && (
          <div className="flex flex-col items-center gap-2 py-3">
            <div className="text-xs text-[var(--theme-mode-text-muted)]">
              Showing {displayResults.length} of {resultsPaging.total}
            </div>
            {resultsPaging.hasMore && (
              <button
                type="button"
                onClick={handleShowMore}
                disabled={loadingMore}
                className="fseg"
              >
                {loadingMore ? 'Loading…' : 'Show more'}
              </button>
            )}
          </div>
        )}

      </div>

      {addVideoFor && (
        <AddLinkModal
          swimmers={[]}
          fixedResultId={addVideoFor.resultId}
          initialSwimmerId={addVideoFor.swimmerId}
          contextLabel={addVideoFor.contextLabel}
          onClose={() => setAddVideoFor(null)}
          onSave={async (input) => {
            const item = await addUserMedia(input);
            if (item) {
              refreshMedia();
              return true;
            }
            return false;
          }}
        />
      )}
    </div>
  );
}

export default ResultsTable;

