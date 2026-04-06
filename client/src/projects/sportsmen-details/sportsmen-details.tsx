import React, { useState, useMemo } from 'react';
import './sportsmen-details.css';
import { useAppSelector } from '../../store/store';
import Helper from '../../utils/helpers/data-helper'
import UI_DateIcon from '../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../components/mix/swimm-style-icon/swimm-style-icon';
import UI_NormativeLevelIcon from '../components/mix/normative-level-icon/normative-level-icon';
import UI_MedalIcon from '../components/mix/medal-icon/medal-icon';
import UI_SwimmerIcon from '../components/mix/swimmer-icon/swimmer-icon';
import UI_PoolIcon from '../components/mix/pool-icon/pool-icon';
import UI_LevelProgress from '../components/mix/progress-level/level-progress';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import UI_RecordCount from '../components/mix/record-count/record-count';
import { recalculatePositions } from '../../utils/helpers/recalculate-positions';

function SportsmenDetails() {
 const filters = useAppSelector((state) => state.filterSelected);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const isMastersSource = !!selectedSource?.is_masters;
  const isAwardSource = !!selectedSource?.is_award;

  if (!selectedSource || !selectedSource.results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  // Применяем recalculation если включён флаг
  const isRecalculated = !!filters.is_recalculated;
  const effectiveResults = useMemo(() => {
    const raw = selectedSource.results ?? [];
    if (!isRecalculated) return raw;
    const recalced = recalculatePositions(raw);
    return recalced.map(r => ({
      ...r,
      position_original: r.position,
      position: r.position_recalc,
    }));
  }, [selectedSource, isRecalculated]);

  const sortedBestResults = Helper.getBestResultsByStyle(effectiveResults, filters.selected_name, isMastersSource);  
  const pointsSum = Helper.getInternationalPointsSumByName(effectiveResults, filters.selected_name);
  const medalCounts = Helper.getMedalCountsByName(effectiveResults, filters.selected_name, isAwardSource);
  const firstResult = sortedBestResults[0];
  //console.log('firstResult sportsmen:', firstResult);

  return (
    <div className="sportsmen-details sticky top-0">
      <div className="mb-4">
        <div className='section-top flex flex-col w-fit mx-auto'>
          <div className='flex flex-row p-4 bg-gray-100'>
            <div className='section-left flex flex-col justify-between items-between'>
              <div className='lg:hidden'>
               <UI_FlagEmoji countryCode="il" size={'40x30'}  />
              </div>
              <div className='hidden lg:block'>
               <UI_FlagEmoji countryCode="il" size={'72x54'}  />
              </div>
              <div className='lg:hidden'>
              <UI_ClubIcon clubName={firstResult.club} iconWidth='10' styleType="icon-notext" className='' />
              </div>
              <div className='hidden lg:block'>
              <UI_ClubIcon clubName={firstResult.club} iconWidth='16' styleType="icon-notext" className='' />
              </div>
            </div>
            <UI_SwimmerIcon swimmerCode='' iconWidth='50' swimmerGender={firstResult.event_style_gender}  />
            <div className='section-right flex flex-col justify-between items-center'>
              <div className='section-normative lg:hidden'>
                <UI_NormativeLevelIcon
                  levelName={firstResult.levelInfo.currentLevel}
                  styleType="style-1"
                  styleSize="size-2"
                  styleName={firstResult.event_style_name}
                  styleLen={firstResult.event_style_len}
                  poolType={firstResult.pool_type}
                  isMasters={Helper.isResultMasters(isMastersSource, firstResult.event_style_age)}
                  normativeAgeGroup={firstResult.levelInfo.normativeAgeGroup}
                />
              </div>
              <div className='section-normative hidden lg:block'>
                <UI_NormativeLevelIcon
                  levelName={firstResult.levelInfo.currentLevel}
                  styleType="style-1"
                  styleSize="size-4"
                  styleName={firstResult.event_style_name}
                  styleLen={firstResult.event_style_len}
                  poolType={firstResult.pool_type}
                  isMasters={Helper.isResultMasters(isMastersSource, firstResult.event_style_age)}
                  normativeAgeGroup={firstResult.levelInfo.normativeAgeGroup}
                />
              </div>
              <UI_RecordCount swimmerName={filters.selected_name} />
              <div className='flex flex-col'>
                 <div className='pl-2 text-lg lg:text-2xl font-bold w-full text-center'>
                    {pointsSum}<sup className='text-base font-normal'>pt</sup>
                </div>
              <div className='flex flex-row'>
               <div className='flex flex-row lg:hidden'>
               <UI_MedalIcon place='1' styleType='icon-place' styleSize='medal-24' placeReplace={medalCounts.first.length.toString()} title={medalCounts.first.map(m => m.note).join('\n')}/>
                <UI_MedalIcon place='2' styleType='icon-place' styleSize='medal-24' placeReplace={medalCounts.second.length.toString()} title={medalCounts.second.map(m => m.note).join('\n')} />
                <UI_MedalIcon place='3' styleType='icon-place' styleSize='medal-24' placeReplace={medalCounts.third.length.toString()} title={medalCounts.third.map(m => m.note).join('\n')} />
              </div>
              <div className='flex flex-row hidden lg:flex'>
               <UI_MedalIcon place='1' styleType='icon-place' placeReplace={medalCounts.first.length.toString()} title={medalCounts.first.map(m => m.note).join('\n')}/>
                <UI_MedalIcon place='2' styleType='icon-place' placeReplace={medalCounts.second.length.toString()} title={medalCounts.second.map(m => m.note).join('\n')} />
                <UI_MedalIcon place='3' styleType='icon-place' placeReplace={medalCounts.third.length.toString()} title={medalCounts.third.map(m => m.note).join('\n')} />
              </div>
                
              </div>
              </div>
             </div>
          </div>
          <div className='px-4 py-2 bg-gray-400'>
          <div className='text-3xl lg:text-5xl font-bold text-right text-white'>{filters.selected_name}</div>
          <div className='text-xl lg:text-2xl font-bold text-right text-white'>{firstResult.event_style_age} year <span className='text-xl'>({firstResult.birth_year})</span></div>
          </div>
        </div>
      </div>
      {sortedBestResults.length > 0 && (
  <div className="mt-6">
    <h2 className="text-xl font-semibold mb-2">Top results by style</h2>
    <TopResultsTabs sortedBestResults={sortedBestResults} isMastersSource={isMastersSource} isAwardSource={isAwardSource} />
  </div>
)}

    </div>
  );
}

// Компонент табов для Training/Competition
function TopResultsTabs({ sortedBestResults, isMastersSource, isAwardSource }: { sortedBestResults: any[]; isMastersSource: boolean; isAwardSource: boolean }) {
  // Разделяем результаты на training и competition
  const trainingResults = useMemo(() => {
    return sortedBestResults.filter(r => !!r.training?.trainingId);
  }, [sortedBestResults]);

  const competitionResults = useMemo(() => {
    return sortedBestResults.filter(r => !r.training?.trainingId);
  }, [sortedBestResults]);

  // Определяем начальный таб: competition, если training пустой
  const initialTab = trainingResults.length === 0 && competitionResults.length > 0 ? 'competition' : 'training';
  const [activeTab, setActiveTab] = useState<'training' | 'competition'>(initialTab);

  // Проверяем, есть ли хотя бы один результат с is_masters
  const hasMasters = isMastersSource;

  // Если нет masters — показываем все результаты без табов
  if (!hasMasters) {
    return (
      <div className="max-h-[50vh] overflow-y-auto border rounded shadow">
        <ResultsTable results={sortedBestResults} isMastersSource={isMastersSource} isAwardSource={isAwardSource} />
      </div>
    );
  }

  const currentResults = activeTab === 'training' ? trainingResults : competitionResults;

  return (
    <div className="max-h-[40vh] overflow-y-auto border rounded shadow">
      {/* Табы */}
      <div className="flex sticky top-0 z-10 bg-white">
        <button
          className={`px-4 py-2 rounded-t border-b-2 ${
            activeTab === 'training'
              ? 'bg-blue-500 text-white border-blue-500'
              : 'bg-gray-100 text-gray-700 border-gray-300'
          }`}
          onClick={() => setActiveTab('training')}
        >
          Training ({trainingResults.length})
        </button>
        <button
          className={`px-4 py-2 rounded-t border-b-2 ml-1 ${
            activeTab === 'competition'
              ? 'bg-blue-500 text-white border-blue-500'
              : 'bg-gray-100 text-gray-700 border-gray-300'
          }`}
          onClick={() => setActiveTab('competition')}
        >
          Competition ({competitionResults.length})
        </button>
      </div>

      {/* Таблица результатов */}
      {currentResults.length > 0 ? (
        <ResultsTable results={currentResults} isMastersSource={isMastersSource} isAwardSource={isAwardSource} />
      ) : (
        <div className="text-gray-500 italic p-4">No {activeTab} results</div>
      )}
    </div>
  );
}

// Вынесенная таблица результатов
function ResultsTable({ results, isMastersSource, isAwardSource }: { results: any[]; isMastersSource: boolean; isAwardSource: boolean }) {
  const hasInternationalPoints = results.some(
    (r) => r.international_points !== undefined && r.international_points !== null && !isNaN(Number(r.international_points))
  );
  const showDate = new Set(results.map((r) => r.date)).size > 1;

  return (
    <div className="max-h-[40vh] overflow-y-auto border rounded shadow">
      <ul className="divide-y">
        {results.map((res, index) => {
          const levelInfo = res.levelInfo;

          return (
            <li key={index} className="px-2 py-2">
              {/* 4-column grid: Position/Age | Time/Style/Pool/Level | Details | Date */}
              <div className="grid grid-cols-[1fr_3fr_4fr] gap-x-3 items-start">

                {/* Column 1: Position + Age */}
                <div className="flex flex-col items-center justify-start">
                  {res.position ? (
                    <UI_MedalIcon place={res.position.toString()} styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} />
                  ) : (
                    <span className="text-lg font-bold">{index + 1}</span>
                  )}
                  {res.event_style_age && (
                    <div className="font-bold text-sm mt-1">
                      <span className="font-normal text-xs">age:</span> {res.event_style_age}
                    </div>
                  )}
                </div>

                {/* Column 2: Time, Style, PoolIcon,  */}
                <div className="flex flex-col items-center gap-1">
                  <div className="text-xl font-bold">{res.time}</div>
                  <UI_SwimmStyleIcon
                    styleName={res.event_style_name}
                    styleLen={res.event_style_len}
                    styleType="icon-len"
                    className="pt-2 font-bold text-base max-w-[96px]"
                  />
                  <UI_PoolIcon
                    styleType="icon-text-center"
                    label={res.pool_type}
                    iconWidth="28"
                    labelClassName="text-xs"
                  />
                  
                </div>

                {/* Column 3: Level, Time split, Points, Date text, LevelProgress */}


                <div className="flex flex-col items-start gap-0.5">
                {levelInfo?.currentLevel ? (
                    <UI_NormativeLevelIcon
                      levelName={levelInfo.currentLevel}
                      styleType="style-1"
                      styleSize="size-1"
                      styleName={res.event_style_name}
                      styleLen={res.event_style_len}
                      poolType={res.pool_type}
                      isMasters={Helper.isResultMasters(isMastersSource, res.event_style_age)}
                      normativeAgeGroup={levelInfo.normativeAgeGroup}
                    />
                  ) : (
                    <span className="text-gray-400">—</span>
                  )}
                  {res.time_split && (
                    <div className="text-sm mb-1">
                      Time split: <span className="text-xs font-bold theme-text-muted">{res.time_split}</span>
                    </div>
                  )}
                  {hasInternationalPoints && (
                    <div className="text-left text-sm">Points: {res.international_points ?? ''}</div>
                  )}
                  {showDate && (
                    <div className="text-left text-sm">
                      Date: <span className="font-semibold">{res.date}</span>
                    </div>
                  )}
                  <UI_LevelProgress
                    styleType="text-only"
                    currentTime={res.time}
                    nextTime={levelInfo?.nextTime}
                    progressPercent={levelInfo?.progressToNextLevel}
                    progressLabel='Progress:'
                  />
                </div>

                {/* Column 4: Date icon */}
                {/* <div className="flex flex-col items-center justify-start">
                  <UI_DateIcon styleType="cube" date={res.date} className="text-xs" paddingClass="px-1 py-1" />
                </div> */}

              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export default SportsmenDetails;
