import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_LevelProgress from '../../components/mix/progress-level/level-progress';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import { ResultsTableRowProps } from './types';

const ResultsTable2xl: React.FC<ResultsTableRowProps> = ({
  res,
  index,
  showAge,
  showClub,
  showEvent,
  showPoolType,
  showDate,
  hasInternationalPoints,
  levelInfo,
  updateFilter,
}) => {
  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  return (
    <>
      <div className="col-span-1 relative">
        {res.position ? <UI_MedalIcon place={res.position.toString()} /> : `${index + 1}`}
        {showAge && <div className="absolute right-0 bottom-0 font-bold">{res.event_style_age}</div>}
      </div>

      <SwimmerNameCell
        firstName={res.first_name}
        lastName={res.last_name}
        club={res.club}
        isRelay={res.is_relay}
        relaySwimmersName={res.relay_swimmers_name}
        onClick={handleNameClick}
        className="col-span-3"
      />

      {showClub && (
        <div className="col-span-1">
          <UI_ClubIcon clubName={res.club} className="text-xs text-center" iconWidth="10" styleType="icon-notext" />
        </div>
      )}

      {showEvent && (
        <div className="col-span-2">
          <div className="max-w-[100px] mx-auto">
            <UI_SwimmStyleIcon styleName={res.event_style_name} styleLen={res.event_style_len} styleType="icon-len" className="font-bold text-2xl" />
          </div>
          {showPoolType && <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} labelClassName="text-xl" />}
        </div>
      )}

      <div className="col-span-1 text-xl font-bold">{res.time}</div>

      {hasInternationalPoints && <div className="col-span-1 text-center">{res.international_points ?? ''}</div>}

      <div className="col-span-1">
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
      </div>

      <div className="col-span-1">
        <UI_LevelProgress
          styleType="progress-bar"
          currentTime={res.time}
          nextTime={levelInfo.nextTime}
          progressPercent={levelInfo.progressToNextLevel}
        />
      </div>

      {showDate && (
        <div className="col-span-1 text-center">
          <UI_DateIcon styleType="cube" date={res.date} paddingClass="px-0 py-1" className="min-w-[64px]" />
        </div>
      )}
    </>
  );
};

export default ResultsTable2xl;
