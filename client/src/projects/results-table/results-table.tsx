import React, { useEffect, useMemo, useState } from 'react';
import './results-table.css';
import { rootActions,useAppDispatch, useAppSelector } from '../../store/store';
import { useFavoritesContext } from '../../hooks/favorites-context';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import Helper from '../../utils/helpers/data-helper'
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
import { recalculatePositions } from '../../utils/helpers/recalculate-positions';
import NormativeAgeRecords from './components/normative-age-records';
import NormativeMastersRecords from './components/normative-masters-records';
import '../components/mix/text-effect/text-effect.css';
import { useResultsLoadMode } from '../../hooks/useResultsLoadMode';
import { useCompetitionMedia } from '../../hooks/useCompetitionMedia';
import { buildResultsFilterParams, fetchResultsPage } from '../../utils/helpers/results-api';
import AddLinkModal from '../my-media-project/components/add-link-modal';
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
    // Пересчитываем и подменяем position (оригинал → position_original)
    const recalced = recalculatePositions(raw);
    return recalced.map(r => ({
      ...r,
      position_original: r.position,      // сохраняем оригинал для отображения
      position: r.position_recalc,         // подменяем position — все фильтры и club points будут работать с ним
    }));
  }, [selectedSource, isRecalculated]);

  // Базовая фильтрация (все фильтры, КРОМЕ level_filter)
  const baseFilteredResults = useMemo(() => sourceResults.filter((res) => {
    const { pool_type, gender, style_name, style_len, date, age, club, activity_type, position_filter, event_date } = filters;
    const resPoolType = Helper.resolvePoolType(res.pool_type);
    const filterPoolType = pool_type === 'all' ? null : Helper.resolvePoolType(pool_type);
    
    // Фильтр по дате события
    if (event_date && event_date !== 'all' && res.date !== event_date) return false;
    
    // Фильтр по training/competition
    const hasTraining = !!res.training?.trainingId;
    const activityType = activity_type || 'training';
    if (activityType === 'training' && !hasTraining) return false;
    if (activityType === 'competition' && hasTraining) return false;

    // Скоуп «мои/избранные» (персональная полоса шапки соревнования, ?filter=).
    // Гостю или без primary — скоуп молча не применяется (пустая таблица хуже).
    const scope = filters.swimmer_scope;
    if (scope === 'my' && isAuthenticated && primarySwimmerId != null) {
      if (res.swimmer_id !== primarySwimmerId) return false;
    } else if (scope === 'favorites' && isAuthenticated && favoriteSwimmerIds.size > 0) {
      if (res.swimmer_id == null || !favoriteSwimmerIds.has(res.swimmer_id)) return false;
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

  const [clubPointsByKey, setClubPointsByKey] = useState<Record<string, number>>({});

  useEffect(() => {
    let cancelled = false;

    const loadClubPoints = async () => {
      const pointsMap = await ClubPointsHelper.getPointsMapForResults(
        sortedResults,
        getResultKey
      );

      if (cancelled) return;
      setClubPointsByKey(pointsMap);
    };

    loadClubPoints();

    return () => {
      cancelled = true;
    };
  }, [sortedResults]);

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
  const poolTypeDisplay = showPoolType ? 'all' : (firstResult?.pool_type ?? filters.pool_type);
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
        {!isMastersSource && (
          <NormativeAgeRecords
            gender={filters.gender}
            poolType={filters.pool_type}
            styleName={filters.style_name}
            styleLen={filters.style_len}
            age={filters.age}
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
        
        <div className="max-h-[650px] overflow-y-auto border border-[var(--theme-mode-border)] rounded-2xl shadow-sm lg:max-w-[1180px] lg:mx-auto" >
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
            {displayResults.map((res, index) => {
              const clubPoints = clubPointsByKey[getResultKey(res)];
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
                age: res.event_style_age,
                isMasters: isMaster,
              };
              const isRecordHolder = Helper.isRecordHolder({ swimmerName, ...recordParams });
              const isRecordTime = Helper.isRecordTime({ time: res.time, ...recordParams });
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

              return (
                <React.Fragment key={index}>
                  <li
                    className={`lg:hidden flex flex-col gap-2 px-3 py-2 rounded border-l-4 border-b border-b-[var(--theme-mode-border-row)] ${isPrimary ? 'border-l-[#f5b800] bg-[var(--theme-mode-me-highlight)]' : `border-l-transparent ${Helper.getGenderBgClass(rowGender)}`}`}
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
                      isExpanded={isRowExpanded}
                      onToggleExpand={() => handleToggleRowExpand(resultKey, isRowExpanded)}
                      onAddVideo={onAddVideo}
                      {...favoriteProps}
                    />
                  </li>

                  <li
                    className={`hidden lg:grid ${isPrimary ? '' : Helper.getGenderBgClass(rowGender)}`}
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

