import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_LevelProgress from '../../components/mix/progress-level/level-progress';
import SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerVideoLink from '../../components/mix/swimmer-video-link/swimmer-video-link';
import { ResultsTableRowProps } from './types';

const ResultsTableDesktop: React.FC<ResultsTableRowProps> = ({
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
      <div className="grid grid-cols-12 gap-2 px-4 pt-3 items-center">
        <div className="col-span-1 relative self-start">
          {res.position ? <UI_MedalIcon place={res.position.toString()} /> : `${index + 1}`}
          {showAge && <div className="absolute right-0 bottom-0 font-bold">{res.event_style_age}</div>}
        </div>

        <div className="col-span-4">
          <SwimmerNameCell
            firstName={res.first_name}
            lastName={res.last_name}
            club={res.club}
            isRelay={res.is_relay}
            relaySwimmersName={res.relay_swimmers_name}
            onClick={handleNameClick}
          />
          <UI_SwimmerVideoLink
            firstNameEn={res.first_name_en}
            lastNameEn={res.last_name_en}
            styleName={res.event_style_name}
            styleLen={res.event_style_len}
            competition={res.competition}
          />

          <div className="w-full flex flex-col items-start justify-center mt-2 mb-2">
            {hasInternationalPoints && <div className="text-left text-sm pt-2">Points: {res.international_points ?? ''}</div>}
             {showDate && (
                <div className="text-left text-sm">
                  Date: <span className="font-semibold">{res.date}</span>
                </div>
              )}
            <UI_LevelProgress
              styleType="text-only"
              currentTime={res.time}
              nextTime={levelInfo.nextTime}
              progressPercent={levelInfo.progressToNextLevel}
            />
          </div>
        </div>

        {showClub && (
          <div className="col-span-1 self-start">
            <UI_ClubIcon clubName={res.club} className="text-xs text-center" iconWidth="10" styleType="icon-notext" />
          </div>
        )}

        <div className="col-span-2 self-start">
          {showEvent && (
            <div className="w-full pr-2">
              <UI_SwimmStyleIcon styleName={res.event_style_name} styleLen={res.event_style_len} styleType="icon-len" className="font-bold text-2xl" />
            </div>
          )}
          {showPoolType && <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} labelClassName="text-base" />}
        </div>

        <div className="col-span-2 text-xl font-bold self-start">{res.time}</div>

        <div className="col-span-2 2xl:col-span-1 self-start">
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

      </div>
    </>
  );
};

export default ResultsTableDesktop;
