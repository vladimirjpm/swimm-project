import React from 'react';
import './results-table.css';
import { rootActions,useAppDispatch, useAppSelector } from '../../store/store';
import Helper from '../../utils/helpers/data-helper'
import UI_DateIcon from '../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../components/mix/swimm-style-icon/swimm-style-icon';
import UI_NormativeLevelIcon from '../components/mix/normative-level-icon/normative-level-icon';
import UI_MedalIcon from '../components/mix/medal-icon/medal-icon';

function ResultsTable() {
  const dispatch = useAppDispatch();
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const filters = useAppSelector((state) => state.filterSelected);

  if (!selectedSource || !selectedSource.results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  const filteredResults = selectedSource.results.filter((res) => {
    const { pool_type, gender, style_name, style_len, date, age, club } = filters;
    
    return (
      (pool_type === 'all' || res.pool_type === pool_type) &&
      (gender === 'all' || res.event_style_gender === gender) &&
      (!style_name || res.event_style_name === style_name) &&
      (!style_len || res.event_style_len === style_len.toString()) &&
      (!date || res.date === date)&&
      (age === 'all' || res.event_style_age?.toString() === age) &&
      (club === 'all' || res.club === club)
    );
  });

  //console.log('filteredResults: ',filteredResults)
  const sortedResults = Helper.sortByTime(filteredResults);
  // Определяем уникальные значения
  const uniqueClubs = new Set(sortedResults.map(r => r.club));
  const uniqueStyleName = new Set(sortedResults.map(r => `${r.event_style_name}-${r.event_style_len}`));
  const uniqueDates = new Set(sortedResults.map(r => r.date));
  const uniqueAge = new Set(sortedResults.map(r => r.event_style_age));

  const showClub = uniqueClubs.size > 1;
  const showEvent = uniqueStyleName.size > 1;
  const showDate = uniqueDates.size > 1;
  const showAge = uniqueAge.size > 1;

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
    <div className="results table">
      <div className="mb-4">

        {selectedSource.title && (
          <h2 className="text-xl font-bold mb-2">{selectedSource.title}</h2>
        )}
        {/* Вынесенные значения */}
       <div className="mb-2 text-sm text-gray-700 flex justify-between items-start">
          {/* Левый столбик: Event и Дата */}
          <div className="flex flex-col space-y-1">
            
            {!showDate && firstResult?.date && (
              <div>            
                <UI_DateIcon styleType="cube" date={firstResult.date} />
              </div>
            )}
          </div>

          {/* Правый блок: Club */}
          {!showClub && firstResult?.club && (
            <div className="pl-4 whitespace-nowrap">
              <UI_ClubIcon clubName={firstResult.club} iconWidth='20' styleType="icon-text-bottom" />
            </div>
          )}
        </div>

      <div className='flex flex-wrap'>
      {
        !showAge && firstResult?.event_style_age && (
          <div>
            <div className='text-6xl'>{firstResult?.event_style_age}</div> 
            <div className='text-4xl'>year</div>
          </div>
        )
      }
      {!showEvent && firstResult?.event && (
        <div className="w-fit mx-auto">
          <UI_SwimmStyleIcon styleName={firstResult.event_style_name}  styleLen={firstResult.event_style_len} styleType='icon-len' className='font-bold text-6xl w-64'/>
        </div>
      )}
      </div>
       
        <div className="max-h-100- overflow-y-auto border rounded shadow">
  <table
    className="table table-auto table-pin-rows w-full border-separate"
    style={{ borderSpacing: '0 0.5rem' }} // добавляет вертикальный отступ между строками
  >
    <thead className="bg-gray-100 sticky top-0 z-10">
      <tr>
        <th className="px-4 py-1">Pos</th>
        <th className="px-2 py-1">Name</th>
        {showClub && <th className="px-2 py-1">Club</th>}
        <th className="px-2 py-1">Time</th>
         {hasInternationalPoints && <th className="px-2 py-1">Points</th>}
        {showEvent && <th className="px-2 py-1">Event</th>}
        <th className="px-2 py-1">Level</th>
        <th className="px-2 py-1">Process</th>
        {showDate && <th className="px-2 py-1">Date</th>}
      </tr>
    </thead>
    <tbody>
      {sortedResults.map((res, index) => {
        const levelInfo = Helper.getNormativeLevelInfo({
          gender: res.event_style_gender === 'male' ? 'male' : 'female',
          poolType: res.pool_type === '25' ? '25m_pool' : '50m_pool',
          styleName: res.event_style_name,
          distance: `${res.event_style_len}m`,
          time: Helper.parseTimeToSeconds(res.time),
          isMaster: !!res.is_masters,
          event_style_age: res.event_style_age,
        });

        return (
          <tr
            key={index}
            /* onClick={() => updateFilter({ selected_name: res.first_name})} */
            
            className={`cursor-pointer border-t ${
              res.event_style_gender === 'female' ? 'bg-pink-100' : 'bg-blue-100'
            }`}
          >
            <td className="px-4 py-1 relative">
              {res.position ? <UI_MedalIcon place={res.position.toString()}   /> : `${index + 1}`}
              {showAge && <div className='absolute right-0 bottom-0 font-bold'>{res.event_style_age}</div>}
            </td>
            <td className="px-2 py-1" onClick={() => updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` })}>
               <div className='text-xl font-bold'>{res.first_name} {res.last_name ? ` - ${res.last_name}` : ''}</div>
               <div className='text-xs'>{res.club}</div>
            </td>
            {showClub && <td className="px-2 py-1"><UI_ClubIcon clubName={res.club} className='text-xs text-center' iconWidth='10' styleType='icon-notext' /></td>}
            
            <td className="px-2 py-1">{res.time}</td>
             {hasInternationalPoints && (
                  <td className="px-2 py-1 text-center">
                    {res.international_points ?? ''}
                  </td>
                )}
            {showEvent && 
              <td className="px-2 py-1 w-28">
                <UI_SwimmStyleIcon styleName={res.event_style_name}  styleLen={res.event_style_len} styleType='icon-len'  className='font-bold text-2xl'/>
              </td>
            }
            <td className="px-2 py-1">
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
            </td>
            <td className="px-2 py-1 w-40">
              <div>
                <span className="text-lg font-bold">{res.time}</span> -&gt;{' '}
                <span className="text-xs">{levelInfo.nextTime}</span>
              </div>
              <div>
                <div className="w-full bg-gray-200 rounded-full dark:bg-gray-700 mt-1">
                  <div
                    className="bg-blue-600 text-xs font-medium text-blue-100 text-center p-0.5 leading-none rounded-full"
                    style={{ width: `${levelInfo.progressToNextLevel}%` }}
                  >
                    {levelInfo.progressToNextLevel}%
                  </div>
                </div>
              </div>
            </td>
            {showDate && <td className="px-2 py-1">{res.date}</td>}
          </tr>
        );
      })}

      {sortedResults.length === 0 && (
        <tr>
          <td colSpan={7} className="text-center text-gray-500 py-4">
            No results match the current filters.
          </td>
        </tr>
      )}
    </tbody>
  </table>
</div>

      </div>
    </div>
  );
}

export default ResultsTable;
function dispatch(arg0: any) {
  throw new Error('Function not implemented.');
}

