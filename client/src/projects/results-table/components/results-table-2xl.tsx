import React from 'react';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { ResultsTableRowProps } from './types';
import UI_AgeLabel from '../../components/mix/age-label/age-label';
import UI_FavoriteControls from '../../components/mix/favorite-controls/favorite-controls';
import UI_PositionBadge from '../../components/mix/position-badge/position-badge';

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
  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  // Медаль красится только если ЭТОТ заплыв award-eligible (res.is_award — денормализовано
  // с API; для статических источников используем общий флаг источника isAwardSource).
  const rowIsAward = res.is_award ?? isAwardSource ?? false;

  return (
    <>
      <div className="col-span-1 flex flex-col items-center">
        {/* При пересчёте (Combine All Results): большой badge — новое место,
            маленький внахлёст снизу — оригинальное место. */}
        <div className="relative">
          <UI_PositionBadge position={res.position} fallbackIndex={index} isAward={rowIsAward} />
          {(res as any).position_original != null && (res as any).position_original !== res.position && (
            <UI_PositionBadge
              position={(res as any).position_original}
              size={18}
              className="absolute -bottom-1 -right-1.5 border-2 border-[var(--theme-mode-surface)]"
              isAward={rowIsAward}
            />
          )}
        </div>
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
            firstLineClassName="text-xl font-bold text-[var(--theme-mode-text)]"
            isRecordHolder={isRecordHolder}
          />
          <UI_FavoriteControls
            className="ml-1"
            swimmerId={res.swimmer_id}
            isFavorite={isFavorite}
            isPrimaryFavorite={isPrimaryFavorite}
            onToggleFavorite={onToggleFavorite}
            onTogglePrimary={onTogglePrimary}
          />
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
          firstLineClassName="text-xl font-bold tabular-nums"
          isRecordHolder={isRecordTime}
        />
      </div>

      {hasInternationalPoints && (
        <div className="col-span-1 text-center">
          {res.international_points ?? ''}
          {/* {clubPoints && clubPoints > 0 ? ` / ${clubPoints}` : ''} */}
        </div>
      )}

      <div className="col-span-1 flex justify-center">
        <UI_NormativeLevelIcon
          levelName={levelInfo.currentLevel}
          styleType="gauge"
          styleSize="size-2"
          styleName={res.event_style_name}
          styleLen={res.event_style_len}
          poolType={res.pool_type}
          isMasters={isMastersResult}
          normativeAgeGroup={levelInfo.normativeAgeGroup}
          progressPercent={levelInfo.progressToNextLevel}
          nextTime={levelInfo.nextTime}
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
