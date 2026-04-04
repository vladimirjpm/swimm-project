import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_LevelProgress from '../../components/mix/progress-level/level-progress';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
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
  isMastersResult,
  isAwardSource,
  isRecordHolder,
}) => {
  const genderBgClass = res.event_style_gender === 'female' ? 'bg-pink-100' : 'bg-blue-100';

  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  return (
    <>
      <div className="flex items-start gap-4">
        <div className="flex flex-col items-center shrink-0">
          {res.position ? <UI_MedalIcon place={res.position.toString()} styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /> : <span className="text-lg font-bold">{index + 1}</span>}
          {(res as any).position_original != null && (res as any).position_original !== res.position && (
            <div className="text-[10px] text-gray-400 mt-0.5 line-through"><UI_MedalIcon place={(res as any).position_original.toString()} styleSize='medal-16' styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /></div>
          )}
          {showAge && <div className="font-bold text-sm mt-1"><span className='font-normal text-xs'>age:</span> {res.event_style_age} </div>}
        </div>

        <div className="flex-1 flex flex-col gap-2">
          <div className="flex items-start">
            <div className="basis-2/3 gap-1 min-w-0">
              <SwimmerNameCell
                firstName={res.first_name}
                lastName={res.last_name}
                club={res.club}
                isRelay={res.is_relay}
                relaySwimmersList={res.relay_swimmers}
                onClick={handleNameClick}
                firstLineClassName="text-xl font-bold truncate1 min-w-0"
                secondLineClassName="text-sm mt-0.5"
                className={genderBgClass}
                showClubIcon={showClub}
                isRecordHolder={isRecordHolder}
              />
              <UI_SwimmerGallery gallery={res.gallery} />
            </div>

            <div className="flex flex-col items-center text-right gap-1 basis-1/3">
              <UI_SwimmerTimeCell
                time={res.time}
                time_split={res.time_split}
                time_fail={res.time_fail}
                time_fail_note={res.time_fail_note}
                firstLineClassName="text-lg font-bold"
                secondLineClassName="text-xs"
                className={`text-right ${genderBgClass}`}
                isRecordHolder={isRecordHolder}
              />
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
            <div className="flex flex-col items-start basis-2/3 gap-1 min-w-0">
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
                isMasters={isMastersResult}
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
