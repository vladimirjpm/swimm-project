import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_LevelProgress from '../../components/mix/progress-level/level-progress';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
import { ResultsTableRowProps } from './types';
import UI_AgeLabel from '../../components/mix/age-label/age-label';

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
  isMastersResult,
  isAwardSource,
  isRecordHolder,
  isRecordTime,
  isPrimaryFavorite,
  isFavorite,
  onToggleFavorite,
  onTogglePrimary,
}) => {
  const genderBgClass = res.event_style_gender === 'female' ? 'bg-pink-100' : 'bg-blue-100';

  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  return (
    <>
      <div className="grid grid-cols-12 gap-2 px-4 pt-3 items-center">
        <div className="col-span-1 flex flex-col items-center self-start">
          {res.position ? <UI_MedalIcon place={res.position.toString()} styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /> : <>{`${index + 1}`}</>}
          {(res as any).position_original != null && (res as any).position_original !== res.position && (
            <div className="text-[10px] text-gray-400 mt-0.5 line-through"><UI_MedalIcon place={(res as any).position_original.toString()} styleSize='medal-16' styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /></div>
          )}
           {showAge && <UI_AgeLabel age={res.event_style_age} isMasters={isMastersResult} ageGroup={res.age_group} />}
        </div>

        <div className={showDate ? 'col-span-3' : 'col-span-4'}>
          <div className="flex items-start gap-1">
            <UI_SwimmerNameCell
              firstName={res.first_name}
              lastName={res.last_name}
              club={res.club}
              isRelay={res.is_relay}
              relaySwimmersList={res.relay_swimmers}
              onClick={handleNameClick}
              className={genderBgClass}
              isRecordHolder={isRecordHolder}
            />
            {onToggleFavorite && (
              <div className="flex flex-col items-center gap-0.5 ml-1 shrink-0">
                <button
                  title={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
                  onClick={() => onToggleFavorite(res.swimmer_id!)}
                  className="text-lg leading-none hover:scale-110 transition-transform"
                >
                  {isFavorite ? '❤️' : '🤍'}
                </button>
                {isFavorite && onTogglePrimary && (
                  <button
                    title={isPrimaryFavorite ? 'Primary favorite' : 'Set as primary'}
                    onClick={() => onTogglePrimary(res.swimmer_id!)}
                    className="text-sm leading-none hover:scale-110 transition-transform"
                  >
                    {isPrimaryFavorite ? '⭐' : '☆'}
                  </button>
                )}
              </div>
            )}
          </div>
          <UI_SwimmerGallery gallery={res.gallery} />

          <div className="w-full flex flex-col items-start justify-center mt-2 mb-2">
            {hasInternationalPoints && <div className="text-left text-sm pt-2">Points: {res.international_points ?? ''}</div>}
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

        <div className="col-span-2 self-start">
          <UI_SwimmerTimeCell
            time={res.time}
            time_split={res.time_split}
            time_fail={res.time_fail}
            time_fail_note={res.time_fail_note}
            firstLineClassName="text-xl font-bold"
            secondLineClassName="text-xs"
            className={genderBgClass}
            isRecordHolder={isRecordTime}
          />
        </div>

        <div className="col-span-2 2xl:col-span-1 self-start">
          <UI_NormativeLevelIcon
            levelName={levelInfo.currentLevel}
            styleType="style-1"
            styleSize="size-2"
            className="text-xs"
            styleName={res.event_style_name}
            styleLen={res.event_style_len}
            poolType={res.pool_type}
            isMasters={isMastersResult}
            normativeAgeGroup={levelInfo.normativeAgeGroup}
          />
        </div>

        {showDate && (
          <div className="col-span-1 self-start text-center">
            <UI_DateIcon styleType="cube" date={res.date} paddingClass="px-0 py-1" className="min-w-[64px]" />
          </div>
        )}

      </div>
    </>
  );
};

export default ResultsTableDesktop;
