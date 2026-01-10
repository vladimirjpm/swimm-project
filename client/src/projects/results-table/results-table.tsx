import React, { useEffect, useMemo, useState } from 'react';
import './results-table.css';
import { rootActions,useAppDispatch, useAppSelector } from '../../store/store';
import Helper from '../../utils/helpers/data-helper'
import ClubPointsHelper from '../../utils/helpers/club-points-helper';
import UI_DateIcon from '../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../components/mix/pool-icon/pool-icon'
import ResultsTableMobile from './components/results-table-mobile';
import ResultsTableDesktop from './components/results-table-desktop';
import ResultsTable2xl from './components/results-table-2xl';
import ResultsHeader from './components/results-header';
import ResultsFilteredInfo from './components/results-filtered-info';

function ResultsTable() {
  const dispatch = useAppDispatch();
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const filters = useAppSelector((state) => state.filterSelected);

  if (!selectedSource || !selectedSource.results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  const filteredResults = useMemo(() => (selectedSource.results ?? []).filter((res) => {
    const { pool_type, gender, style_name, style_len, date, age, club, activity_type } = filters;
    const resPoolType = Helper.resolvePoolType(res.pool_type);
    const filterPoolType = pool_type === 'all' ? null : Helper.resolvePoolType(pool_type);
    
    // Фильтр по training/competition
    const hasTraining = !!res.training?.trainingId;
    const activityType = activity_type || 'training';
    if (activityType === 'training' && !hasTraining) return false;
    if (activityType === 'competition' && hasTraining) return false;
    
    return (
      (!filterPoolType || resPoolType === filterPoolType) &&
      (gender === 'all' || res.event_style_gender === gender) &&
      (!style_name || res.event_style_name === style_name) &&
      (!style_len || res.event_style_len === style_len.toString()) &&
      (!date || res.date === date)&&
      (age === 'all' || res.event_style_age?.toString() === age) &&
      (club === 'all' || res.club === club)
    );
  }), [selectedSource, filters]);

  //console.log('filteredResults: ',filteredResults)
  const sortedResults = useMemo(
    () => Helper.sortByTime(filteredResults),
    [filteredResults],
  );

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

  const [clubPointsByKey, setClubPointsByKey] = useState<Record<string, number>>({});

  useEffect(() => {
    let cancelled = false;

    const loadClubPoints = async () => {
      const entries = await Promise.all(
        sortedResults.map(async (res) => {
          const key = getResultKey(res);
          const points = await ClubPointsHelper.getPointsForResult(res);
          return [key, points] as const;
        }),
      );

      if (cancelled) return;

      const next: Record<string, number> = {};
      for (const [key, points] of entries) {
        next[key] = points;
      }
      setClubPointsByKey(next);
    };

    loadClubPoints();

    return () => {
      cancelled = true;
    };
  }, [sortedResults]);
  //console.log('sortedResults: ',sortedResults)
  // Определяем уникальные значения
  const uniqueClubs = new Set(sortedResults.map(r => r.club));
  const uniqueStyleName = new Set(sortedResults.map(r => `${r.event_style_name}-${r.event_style_len}`));
  const uniqueDates = new Set(sortedResults.map(r => r.date));
  const uniqueAge = new Set(sortedResults.map(r => r.event_style_age));
  const uniquePoolType = new Set(sortedResults.map(r => Helper.resolvePoolType(r.pool_type)));

  const showClub = uniqueClubs.size > 1;
  const showEvent = uniqueStyleName.size > 1;
  const showDate = uniqueDates.size > 1;
  const showAge = uniqueAge.size > 1;
  const showPoolType = uniquePoolType.size > 1;

  const hasInternationalPoints = sortedResults.some(r => 
  r.international_points !== undefined &&
  r.international_points !== null &&
  !isNaN(Number(r.international_points))
);

  const firstResult = sortedResults[0];
// Функция обновления фильтров
  const updateFilter = (newFilter: Partial<typeof filters>) => {
      dispatch(rootActions.updateState({ filterSelected: { ...filters, ...newFilter } }));
  };

  return (
    <div className="results table w-full">
      <div className="mb-4">

        {selectedSource.title && (
          <h2 className="text-center text-2xl font-bold mt-4 lg:mt-0 mb-2">
            <div className='effect-super-bold1 py-4 theme-bg-header'>{selectedSource.title}</div>
          </h2>
        )}
        <ResultsFilteredInfo
          firstResult={firstResult}
          showDate={showDate}
          showClub={showClub}
          showAge={showAge}
          showPoolType={showPoolType}
          showEvent={showEvent}
        />      
        <div className="max-h-[650px] overflow-y-auto border rounded shadow" >
          {/* Unified header (single view for all sizes) */}
          <div className="bg-gray-100 sticky top-0 z-10">
            <div className="hidden lg:grid 2xl:hidden">
              <ResultsHeader view="desktop" showClub={showClub} showEvent={showEvent} showDate={showDate} hasInternationalPoints={hasInternationalPoints} />
            </div>
            <div className="hidden 2xl:grid">
              <ResultsHeader view="2xl" showClub={showClub} showEvent={showEvent} showDate={showDate} hasInternationalPoints={hasInternationalPoints} />
            </div>
            <div className="lg:hidden">
              <ResultsHeader view="mobile" showClub={showClub} showEvent={showEvent} showDate={showDate} hasInternationalPoints={hasInternationalPoints} />
            </div>
          </div>
          <ul className="divide-y">
            {sortedResults.map((res, index) => {
              const clubPoints = clubPointsByKey[getResultKey(res)];
              const isMaster = String(res.is_masters) === 'true' || String(res.is_masters) === '1';
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

              return (
                <React.Fragment key={index}>
                  <li
                    className={`lg:hidden flex flex-col gap-2 px-3 py-2 rounded ${Helper.getGenderBgClass(res.event_style_gender)}`}
                  >
                    <ResultsTableMobile
                      res={res}
                      index={index}
                      showAge={showAge}
                      showClub={showClub}
                      showEvent={showEvent}
                      showPoolType={showPoolType}
                      showDate={showDate}
                      hasInternationalPoints={hasInternationalPoints}
                      clubPoints={clubPoints}
                      levelInfo={levelInfo}
                      updateFilter={updateFilter}
                    />
                  </li>

                  <li
                    className={`hidden lg:grid 2xl:hidden ${Helper.getGenderBgClass(res.event_style_gender)}`}
                  >
                    <ResultsTableDesktop
                      res={res}
                      index={index}
                      showAge={showAge}
                      showClub={showClub}
                      showEvent={showEvent}
                      showPoolType={showPoolType}
                      showDate={showDate}
                      hasInternationalPoints={hasInternationalPoints}
                      clubPoints={clubPoints}
                      levelInfo={levelInfo}
                      updateFilter={updateFilter}
                    />
                  </li>

                  <li
                    className={`hidden 2xl:grid grid-cols-12 gap-2 px-4 py-3 items-center ${Helper.getGenderBgClass(res.event_style_gender)}`}
                  >
                    <ResultsTable2xl
                      res={res}
                      index={index}
                      showAge={showAge}
                      showClub={showClub}
                      showEvent={showEvent}
                      showPoolType={showPoolType}
                      showDate={showDate}
                      hasInternationalPoints={hasInternationalPoints}
                      clubPoints={clubPoints}
                      levelInfo={levelInfo}
                      updateFilter={updateFilter}
                    />
                  </li>
                </React.Fragment>
              );
            })}

            {sortedResults.length === 0 && (
              <li className="text-center text-gray-500 py-4">No results match the current filters.</li>
            )}
          </ul>
        </div>

      </div>
    </div>
  );
}

export default ResultsTable;

