import React from 'react';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import { Result } from '../../../utils/interfaces/results';

interface ResultsFilteredInfoProps {
  firstResult: Result | undefined;
  showDate: boolean;
  showClub: boolean;
  showAge: boolean;
  showPoolType: boolean;
  showEvent: boolean;
}

function ResultsFilteredInfo({
  firstResult,
  showDate,
  showClub,
  showAge,
  showPoolType,
  showEvent,
}: ResultsFilteredInfoProps) {
  return (
    <div className="show-filtered-data">
      <ul className="grid grid-cols-6 gap-2 mb-4 max-w-3xl mx-auto items-start">
        {/* Date */}
        <li className="flex flex-col items-center h-full">
          <span className="text-xs text-gray-500 uppercase mb-1">Date</span>
          <div className="flex-1 flex items-center justify-center">
            {showDate ? (
              <span className="text-lg font-semibold">All</span>
            ) : (
              firstResult?.date && <UI_DateIcon paddingClass='px-1 py-1' className='text-xs' styleType="cube" date={firstResult.date} />
            )}
          </div>
        </li>

        {/* Club */}
        <li className="flex flex-col items-center h-full">
          <span className="text-xs text-gray-500 uppercase mb-1">Club</span>
          <div className="flex-1 flex items-center justify-center">
            {showClub ? (
              <span className="text-lg font-semibold">All</span>
            ) : (
              firstResult?.club && <UI_ClubIcon clubName={firstResult.club} className='text-xs' iconWidth="10" styleType="icon-text-bottom" />
            )}
          </div>
        </li>

        {/* Event - занимает 2 колонки, по центру */}
        <li className="col-span-2 flex flex-col items-center h-full">
          <span className="text-xs text-gray-500 uppercase mb-1">Event</span>
          <div className="flex-1 flex items-center justify-center max-w-[100px]">
            {showEvent ? (
              <span className="text-lg font-semibold">All</span>
            ) : (
              firstResult?.event && (
                <UI_SwimmStyleIcon
                  styleName={firstResult.event_style_name}
                  styleLen={firstResult.event_style_len}
                  styleType="icon-len"
                  className="font-bold text-2xl"
                />
              )
            )}
          </div>
        </li>

        {/* Age */}
        <li className="flex flex-col items-center h-full">
          <span className="text-xs text-gray-500 uppercase mb-1">Age</span>
          <div className="flex-1 flex items-center justify-center">
            {showAge ? (
              <span className="text-lg font-semibold">All</span>
            ) : (
              firstResult?.event_style_age && (
                <span className="text-2xl font-bold">{firstResult.event_style_age}</span>
              )
            )}
          </div>
        </li>

        {/* Pool */}
        <li className="flex flex-col items-center h-full">
          <span className="text-xs text-gray-500 uppercase mb-1">Pool</span>
          <div className="flex-1 flex items-center justify-center">
            {showPoolType ? (
              <span className="text-lg font-semibold">All</span>
            ) : (
              firstResult?.pool_type && <UI_PoolIcon styleType="icon-text-top" label={firstResult.pool_type} iconWidth="40" labelClassName="text-sm" />
            )}
          </div>
        </li>
      </ul>
    </div>
  );
}

export default ResultsFilteredInfo;
