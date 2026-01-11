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
import FlagEmoji from '../components/mix/flag-icon/flag-icon';

function SportsmenDetails() {
 const filters = useAppSelector((state) => state.filterSelected);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);

  if (!selectedSource || !selectedSource.results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  const sortedBestResults = Helper.getBestResultsByStyle(selectedSource.results, filters.selected_name);  
  const pointsSum = Helper.getInternationalPointsSumByName(selectedSource.results, filters.selected_name);
  const medalCounts =  Helper.getMedalCountsByName(selectedSource.results, filters.selected_name);
  const firstResult = sortedBestResults[0];
  //console.log('firstResult sportsmen:', firstResult);

  return (
    <div className="sportsmen-details sticky top-0">
      <div className="mb-4">
        <div className='section-top flex flex-col w-fit mx-auto'>
          <div className='flex flex-row p-4 bg-gray-100'>
            <div className='flex flex-col justify-between items-center '>
              <FlagEmoji countryCode="il" size={'72x54'}  />
              <UI_ClubIcon clubName={firstResult.club} iconWidth='24' styleType="icon-notext" className='px-3' />
            </div>
            <UI_SwimmerIcon swimmerCode='' iconWidth='50' swimmerGender={firstResult.event_style_gender}  />
            <div className='flex flex-col justify-between items-center'>
              <UI_NormativeLevelIcon
                levelName={firstResult.levelInfo.currentLevel}
                styleType="style-1"
                styleSize="size-5"
                styleName={firstResult.event_style_name}
                styleLen={firstResult.event_style_len}
                poolType={firstResult.pool_type}
                isMasters={firstResult.is_masters}
                normativeAgeGroup={firstResult.levelInfo.normativeAgeGroup}
              />
              <div className='flex flex-col'>
                 <div className='pl-2 text-2xl font-bold w-full text-center'>
                    {pointsSum}<sup className='text-base font-normal'>pt</sup>
                </div>
              <div className='flex flex-row'>
               
                <UI_MedalIcon place='1' styleType='icon-place' placeReplace={medalCounts.first.length.toString()} title={medalCounts.first.map(m => m.note).join('\n')}/>
                <UI_MedalIcon place='2' styleType='icon-place' placeReplace={medalCounts.second.length.toString()} title={medalCounts.second.map(m => m.note).join('\n')} />
                <UI_MedalIcon place='3' styleType='icon-place' placeReplace={medalCounts.third.length.toString()} title={medalCounts.third.map(m => m.note).join('\n')} />
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
    <TopResultsTabs sortedBestResults={sortedBestResults} />
  </div>
)}

    </div>
  );
}

// Компонент табов для Training/Competition
function TopResultsTabs({ sortedBestResults }: { sortedBestResults: any[] }) {
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
  const hasMasters = useMemo(() => {
    return sortedBestResults.some(r => String(r.is_masters) === 'true' || String(r.is_masters) === '1');
  }, [sortedBestResults]);

  // Если нет masters — показываем все результаты без табов
  if (!hasMasters) {
    return (
      <div className="max-h-[50vh] overflow-y-auto border rounded shadow">
        <ResultsTable results={sortedBestResults} />
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
        <ResultsTable results={currentResults} />
      ) : (
        <div className="text-gray-500 italic p-4">No {activeTab} results</div>
      )}
    </div>
  );
}

// Вынесенная таблица результатов
function ResultsTable({ results }: { results: any[] }) {
  return (
    <div className="max-h-[40vh] overflow-y-auto border rounded shadow">
      <table
        className="table table-auto table-pin-rows w-full border-separate"
        style={{ borderSpacing: '0 0.5rem' }}
      >
        <thead className="bg-gray-100 sticky top-0 z-10">
          <tr>
            <th className="px-4 py-2">Style</th>
            <th className="px-4 py-2">Time</th>
            <th className="px-4 py-2">Level</th>
            <th className="px-4 py-2">Progress</th>
            <th className="px-4 py-2">Date</th>
          </tr>
        </thead>
        <tbody>
          {results.map((res, index) => {
            const levelInfo = res.levelInfo;
            const progress = levelInfo?.progressToNextLevel ?? 0;

            return (
              <tr key={index} className="bg-gray-50 border-t">
                <td className="px-4 py-2 whitespace-nowrap w-28">
                  <UI_SwimmStyleIcon
                    styleName={res.event_style_name}
                    styleLen={res.event_style_len}
                    styleType="icon-len"
                  />
                </td>
                <td className="px-4 py-2 font-mono">
                  {res.time}
                </td>
                <td className="px-4 py-2">
                    {levelInfo?.currentLevel ? (
                    <UI_NormativeLevelIcon
                      levelName={levelInfo.currentLevel}
                      styleType="style-1"
                      styleSize="size-2"
                      styleName={res.event_style_name}
                      styleLen={res.event_style_len}
                      poolType={res.pool_type}
                      isMasters={res.is_masters}
                      normativeAgeGroup={levelInfo.normativeAgeGroup}
                    />
                  ) : (
                    <span className="text-gray-400">—</span>
                  )}
                </td>
                <td className="px-4 py-2">
                  <div>
                    <span className="text-lg font-bold">
                      {res.time}
                    </span>{' '}
                    →{' '}
                    <span className="text-xs">
                      {levelInfo?.nextTime ?? '–'}
                    </span>
                  </div>
                  <div className="w-full bg-gray-200 rounded-full mt-1">
                    <div
                      className="bg-blue-600 text-xs font-medium text-blue-100 text-center p-0.5 leading-none rounded-full"
                      style={{ width: `${progress}%` }}
                    >
                      {progress}%
                    </div>
                  </div>
                </td>
                <td className="px-4 py-2">
                  <UI_DateIcon styleType="cube" date={res.date} className='text-xs' paddingClass="px-1 py-1"/>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export default SportsmenDetails;
