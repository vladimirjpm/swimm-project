import React from 'react';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
import { ResultsTableRowProps, buildResultsGridTemplate } from './types';
import UI_AgeLabel from '../../components/mix/age-label/age-label';
import UI_FavoriteControls from '../../components/mix/favorite-controls/favorite-controls';
import UI_PositionBadge from '../../components/mix/position-badge/position-badge';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import ResultRowDateInfo from './result-row-date-info';
import UI_AddVideoIcon from '../../components/mix/add-video-icon/add-video-icon';

const ResultsTableDesktop: React.FC<ResultsTableRowProps> = ({
  res,
  index,
  showAge,
  showClub,
  showEvent,
  showPoolType,
  showDate,
  showCompetition,
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
  onAddVideo,
}) => {
  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  const gridTemplate = buildResultsGridTemplate({ showClub, showEvent, showPoolType, showDate, hasInternationalPoints });

  // Медаль красится только если ЭТОТ заплыв award-eligible (res.is_award — денормализовано
  // с API; для статических источников используем общий флаг источника isAwardSource).
  const rowIsAward = res.is_award ?? isAwardSource ?? false;

  return (
    <div
      className={`grid gap-[14px] px-6 py-[13px] items-center border-b border-[var(--theme-mode-border-row)] border-l-4 ${isPrimaryFavorite ? 'border-l-[#f5b800] bg-[var(--theme-mode-me-highlight)]' : 'border-l-transparent'}`}
      style={{ gridTemplateColumns: gridTemplate }}
    >
      {/* FAV / ME (ведущая колонка) */}
      <div className="flex flex-col items-center justify-center self-center gap-1">
        <UI_FavoriteControls
          swimmerId={res.swimmer_id}
          isFavorite={isFavorite}
          isPrimaryFavorite={isPrimaryFavorite}
          onToggleFavorite={onToggleFavorite}
          showPrimary={false}
        />
        {onAddVideo && (
          <button
            type="button"
            title="Add video"
            onClick={onAddVideo}
            className="flex items-center justify-center w-4 h-4 text-[var(--theme-mode-text-muted)] hover:text-[var(--theme-primary)]"
          >
            <UI_AddVideoIcon size={14} />
          </button>
        )}
      </div>

      {/* POS */}
      <div className="flex flex-col items-center self-center gap-1">
        {/* При пересчёте (Combine All Results): большой badge — новое место,
            маленький внахлёст снизу — оригинальное место. */}
        <div className="relative">
          {/* Место — только из БД (при Combine All Results сюда уже подставлен position_recalc,
              оригинал — в маленьком бейдже ниже). Никаких фолбэков на номер строки. */}
          <UI_MedalIcon place={String(res.position ?? '—')} styleType={rowIsAward ? 'icon-place' : 'icon-noplace'} styleSize="medal-40" />
          {(res as any).position_original != null && (res as any).position_original !== res.position && (
            <UI_PositionBadge
              position={(res as any).position_original}
              size={18}
              className="absolute -bottom-1 -right-1.5 border-2 border-[var(--theme-mode-surface)]"
              isAward={rowIsAward}
            />
          )}
        </div>
        {showAge && <UI_AgeLabel age={res.event_style_age} eventCategory={res.event_category} isMasters={isMastersResult} ageGroup={res.age_group} className="items-center text-[var(--theme-mode-text-muted)] [&>div]:text-[9px] [&>div]:mt-0 [&>div]:font-bold [&_span]:text-[9px] [&_span]:font-bold" />}
      </div>

      {/* SWIMMER */}
      <div className="min-w-0">
        <UI_SwimmerNameCell
          firstName={res.first_name}
          lastName={res.last_name}
          club={res.club}
          isRelay={res.is_relay}
          relaySwimmersList={res.relay_swimmers}
          relaySwimmersName={res.relay_swimmers_name}
          onClick={handleNameClick}
          firstLineClassName="text-[15px] font-bold overflow-hidden text-[var(--theme-mode-text)]"
          secondLineClassName="text-xs text-[var(--theme-mode-text-secondary)]"
          isRecordHolder={isRecordHolder}
          isMe={isPrimaryFavorite}
        />
        {showDate && (
          <ResultRowDateInfo date={res.date} competition={res.competition} showCompetition={showCompetition} />
        )}
        <UI_SwimmerGallery gallery={res.gallery} />
      </div>

      {/* CLUB (иконка — отдельной колонкой; размер обёрткой, см. mobile) */}
      {showClub && (
        <div className="self-center flex justify-center">
          <div style={{ width: 30, height: 30 }}>
            <UI_ClubIcon clubName={res.club} iconWidth="full" styleType="icon-notext" />
          </div>
        </div>
      )}

      {/* STYLE (иконка стиля/дистанция — размер ограничен колонкой) */}
      {(showEvent || showPoolType) && (
        <div className="self-center w-[88px] mx-auto [&_img]:w-full [&_img]:h-auto">
          {showEvent && (
            <UI_SwimmStyleIcon styleName={res.event_style_name} styleLen={res.event_style_len} styleType="icon-len" className="font-bold text-base" />
          )}
          {showPoolType && <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} labelClassName="text-xs" />}
        </div>
      )}

      {/* TIME */}
      <div className="self-center text-right pl-4">
        <UI_SwimmerTimeCell
          time={res.time}
          time_split={res.time_split}
          time_fail={res.time_fail}
          time_fail_note={res.time_fail_note}
          firstLineClassName="text-[21px] font-bold tabular-nums tracking-tight flex justify-start"
          secondLineClassName="text-xs justify-start"
          isRecordHolder={isRecordTime}
          isSuspect={!!res.suspect_reason}
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
