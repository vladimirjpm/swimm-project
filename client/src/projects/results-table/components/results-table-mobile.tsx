import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_LevelProgress from '../../components/mix/progress-level/level-progress';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerVideoLink from '../../components/mix/swimmer-video-link/swimmer-video-link';
import { ResultsTableRowProps } from './types';

const ResultsTableMobile: React.FC<ResultsTableRowProps> = ({
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
      <div className="flex items-start gap-4">
        <div className="flex flex-col items-center shrink-0">
          {res.position ? <UI_MedalIcon place={res.position.toString()} /> : <span className="text-lg font-bold">{index + 1}</span>}
          {showAge && <div className="font-bold text-sm mt-1"><span className='font-normal text-xs'>age:</span> {res.event_style_age} </div>}
        </div>

        <div className="flex-1 flex flex-col gap-2">
          <div className="flex items-start">
            <div className="basis-2/3 pr-2">
              <SwimmerNameCell
                firstName={res.first_name}
                lastName={res.last_name}
                club={res.club}
                isRelay={res.is_relay}
                relaySwimmersName={res.relay_swimmers_name}
                onClick={handleNameClick}
                firstLineClassName="text-xl font-bold truncate"
                secondLineClassName="text-sm mt-0.5"
                showClubIcon={showClub}
              />
              <UI_SwimmerVideoLink
                firstNameEn={res.first_name_en}
                lastNameEn={res.last_name_en}
                styleName={res.event_style_name}
                styleLen={res.event_style_len}
                competition={res.competition}
              />
            </div>

            <div className="flex flex-col items-center text-right gap-1 basis-1/3">
              <div className="text-lg font-bold">{res.time}</div>
              {showEvent && (
                <UI_SwimmStyleIcon
                  styleName={res.event_style_name}
                  styleLen={res.event_style_len}
                  styleType="icon-len"
                  className="font-bold text-lg max-w-[96px]"
                />
              )}
              {showPoolType && (
                <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} iconWidth="32" labelClassName="text-xs" />
              )}
            </div>
          </div>

          <div className="w-full flex items-start gap-3 mb-2">
            <div className="flex flex-col items-start basis-2/3 pr-2">
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
                nextTime={levelInfo.nextTime}
                progressPercent={levelInfo.progressToNextLevel}
              />
            </div>

            <div className="flex justify-center basis-1/3">
              <UI_NormativeLevelIcon
                levelName={levelInfo.currentLevel}
                styleType="style-1"
                styleSize="size-1"
                styleName={res.event_style_name}
                styleLen={res.event_style_len}
                poolType={res.pool_type}
                isMasters={res.is_masters}
                normativeAgeGroup={levelInfo.normativeAgeGroup}
              />
            </div>
          </div>
        </div>
      </div>
    </>
  );
};

export default ResultsTableMobile;
