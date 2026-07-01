import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_LevelProgress from '../../components/mix/progress-level/level-progress';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { ResultsTableRowProps } from './types';
import UI_AgeLabel from '../../components/mix/age-label/age-label';

const ResultsTable2xl: React.FC<ResultsTableRowProps> = ({
  res,
  index,
  showAge,
  showClub,
  showEvent,
  showPoolType,
  showDate,
  hasInternationalPoints,
  clubPoints,
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
  const genderBgClass = res.event_style_gender === 'female' ? 'bg-[var(--theme-mode-row-female)]' : 'bg-[var(--theme-mode-row-male)]';

  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  return (
    <>
      <div className="col-span-1 flex flex-col items-center">
        {res.position ? <UI_MedalIcon place={res.position.toString()} styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /> : <>{`${index + 1}`}</>}
        {(res as any).position_original != null && (res as any).position_original !== res.position && (
          <div className="text-[10px] text-[var(--theme-mode-text-muted)] mt-0.5 line-through"><UI_MedalIcon place={(res as any).position_original.toString()} styleSize='medal-16' styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /></div>
        )}
        {showAge && <UI_AgeLabel age={res.event_style_age} isMasters={isMastersResult} ageGroup={res.age_group} />}
      </div>

      <div className='flex flex-col col-span-3'>
        <div className="flex items-start gap-1">
          <UI_SwimmerNameCell
            firstName={res.first_name}
            lastName={res.last_name}
            firstNameEn={res.first_name_en}
            lastNameEn={res.last_name_en}
            club={res.club}
            isRelay={res.is_relay}
            relaySwimmersList={res.relay_swimmers}
            relaySwimmersName={res.relay_swimmers_name}
            onClick={handleNameClick}
            className={genderBgClass}
            isRecordHolder={isRecordHolder}
          />
          {onToggleFavorite && (
            <div className="flex flex-col items-center gap-0.5 shrink-0 ml-1">
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
      </div>

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

      <div className="col-span-1">
        <UI_SwimmerTimeCell
          time={res.time}
          time_split={res.time_split}
          time_fail={res.time_fail}
          time_fail_note={res.time_fail_note}
          className={genderBgClass}
          isRecordHolder={isRecordTime}
        />
      </div>

      {hasInternationalPoints && (
        <div className="col-span-1 text-center">
          {res.international_points ?? ''}
          {/* {clubPoints && clubPoints > 0 ? ` / ${clubPoints}` : ''} */}
        </div>
      )}

      <div className="col-span-1">
        <UI_NormativeLevelIcon
          levelName={levelInfo.currentLevel}
          styleType="style-1"
          styleSize="size-2"
          styleName={res.event_style_name}
          styleLen={res.event_style_len}
          poolType={res.pool_type}
          isMasters={isMastersResult}
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
