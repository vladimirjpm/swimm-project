import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
import { ResultsTableRowProps, buildResultsGridTemplate } from './types';
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
  const genderBgClass = res.event_style_gender === 'female' ? 'bg-[var(--theme-mode-row-female)]' : 'bg-[var(--theme-mode-row-male)]';

  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  const gridTemplate = buildResultsGridTemplate({ showClub, showEvent, showPoolType, showDate, hasInternationalPoints });

  return (
    <div
      className={`grid gap-3 px-6 py-3 items-center border-b border-[var(--theme-mode-border-row)] ${genderBgClass}`}
      style={{ gridTemplateColumns: gridTemplate }}
    >
      {/* POS */}
      <div className="flex flex-col items-center self-center gap-1">
        {res.position ? <UI_MedalIcon place={res.position.toString()} styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /> : <>{`${index + 1}`}</>}
        {(res as any).position_original != null && (res as any).position_original !== res.position && (
          <div className="text-[10px] text-[var(--theme-mode-text-muted)] mt-0.5 line-through"><UI_MedalIcon place={(res as any).position_original.toString()} styleSize='medal-16' styleType={isAwardSource ? 'icon-place' : 'icon-noplace'} /></div>
        )}
        {showAge && <UI_AgeLabel age={res.event_style_age} isMasters={isMastersResult} ageGroup={res.age_group} />}
      </div>

      {/* SWIMMER */}
      <div className="min-w-0">
        <div className="flex items-start gap-1">
          <UI_SwimmerNameCell
            firstName={res.first_name}
            lastName={res.last_name}
            club={res.club}
            isRelay={res.is_relay}
            relaySwimmersList={res.relay_swimmers}
            relaySwimmersName={res.relay_swimmers_name}
            onClick={handleNameClick}
            firstLineClassName="text-[15px] font-bold overflow-hidden"
            secondLineClassName="text-xs text-[var(--theme-mode-text-secondary)]"
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
      </div>

      {/* CLUB */}
      {showClub && (
        <div className="self-center">
          <UI_ClubIcon clubName={res.club} className="text-xs text-center" iconWidth="10" styleType="icon-notext" />
        </div>
      )}

      {/* STYLE (иконка стиля/дистанция — без изменений) */}
      {(showEvent || showPoolType) && (
        <div className="self-center">
          {showEvent && (
            <div className="w-full pr-2">
              <UI_SwimmStyleIcon styleName={res.event_style_name} styleLen={res.event_style_len} styleType="icon-len" className="font-bold text-2xl" />
            </div>
          )}
          {showPoolType && <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} labelClassName="text-base" />}
        </div>
      )}

      {/* TIME */}
      <div className="self-center text-right">
        <UI_SwimmerTimeCell
          time={res.time}
          time_split={res.time_split}
          time_fail={res.time_fail}
          time_fail_note={res.time_fail_note}
          firstLineClassName="text-[21px] font-bold tabular-nums tracking-tight flex justify-end"
          secondLineClassName="text-xs"
          isRecordHolder={isRecordTime}
        />
      </div>

      {/* LEVEL (gauge) */}
      <div className="self-center flex justify-center">
        <UI_NormativeLevelIcon
          levelName={levelInfo.currentLevel}
          styleType="gauge"
          styleSize="size-2"
          className="text-xs"
          styleName={res.event_style_name}
          styleLen={res.event_style_len}
          poolType={res.pool_type}
          isMasters={isMastersResult}
          normativeAgeGroup={levelInfo.normativeAgeGroup}
          progressPercent={levelInfo.progressToNextLevel}
          nextTime={levelInfo.nextTime}
        />
      </div>

      {/* DATE */}
      {showDate && (
        <div className="self-center text-center">
          <UI_DateIcon styleType="cube" date={res.date} paddingClass="px-0 py-1" className="min-w-[64px]" />
        </div>
      )}

      {/* PTS */}
      {hasInternationalPoints && (
        <div className="self-center text-right">
          <div className="text-base font-extrabold text-[var(--theme-mode-text)] tabular-nums leading-none">{res.international_points ?? ''}</div>
          <div className="text-[8.5px] font-bold tracking-wide text-[var(--theme-mode-text-muted)] mt-0.5">PTS</div>
        </div>
      )}
    </div>
  );
};

export default ResultsTableDesktop;
