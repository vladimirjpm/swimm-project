import React from 'react';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import UI_SwimmerTimeCell from '../../components/mix/swimmer-time-cell/swimmer-time-cell';
import UI_AgeLabel from '../../components/mix/age-label/age-label';
import UI_FavoriteControls from '../../components/mix/favorite-controls/favorite-controls';
import UI_PositionBadge from '../../components/mix/position-badge/position-badge';
import UI_PrelimLabel from '../../components/mix/prelim-label/prelim-label';
import UI_RoundLabel from '../../components/mix/round-label/round-label';
import { ResultsTableRowProps } from './types';
import ResultRowDateInfo from './result-row-date-info';
import UI_AddVideoIcon from '../../components/mix/add-video-icon/add-video-icon';
import HelperResults from '../../../utils/helpers/helper-results';
import UI_SeasonBestBadge from '../../components/mix/season-best-badge/season-best-badge';

const ResultsTableMobile: React.FC<ResultsTableRowProps> = ({
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
  recordHolderMark,
  recordTimeMark,
  isSeasonBestTime,
  isPrimaryFavorite,
  isFavorite,
  onToggleFavorite,
  onTogglePrimary,
  isExpanded,
  onToggleExpand,
  onAddVideo,
}) => {
  const handleNameClick = () => {
    updateFilter({ selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}` });
  };

  // Медаль красится только если ЭТОТ заплыв award-eligible (res.is_award — денормализовано
  // с API; для статических источников используем общий флаг источника isAwardSource).
  // Медаль только за награждаемый заплыв: prelim-место — ранжир сессии, не награда.
  const rowIsAward = (res.is_award ?? isAwardSource ?? false) && !HelperResults.isHiddenHeat(res.heat_type);

  return (
    <div className="cursor-pointer" onClick={onToggleExpand}>
      <div className="flex items-start gap-4">
        {/* Левая колонка: место → age → сердечко (избранное) */}
        <div className="flex flex-col items-center gap-1 shrink-0">
          {/* При пересчёте (Combine All Results): большой badge — новое место,
              маленький внахлёст снизу — оригинальное место. */}
          <div className="relative">
            {/* Место — только из БД (при Combine All Results — position_recalc), без фолбэка на номер строки. */}
            <UI_PositionBadge position={res.position ?? '—'} size={36} isAward={rowIsAward} />
            {(res as any).position_original != null && (res as any).position_original !== res.position && (
              <UI_PositionBadge
                position={(res as any).position_original}
                size={18}
                className="absolute -bottom-1 -right-1.5 border-2 border-[var(--theme-mode-surface)]"
                isAward={rowIsAward}
              />
            )}
          </div>
          <UI_PrelimLabel heatType={res.heat_type} />
          <UI_RoundLabel round={res.round} />
          {showAge && <UI_AgeLabel age={HelperResults.ageLabel(res)} eventCategory={res.event_category} isRelay={res.is_relay} gender={res.event_style_gender} isMasters={isMastersResult} ageGroup={res.age_group} />}
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
              onClick={(e) => { e.stopPropagation(); onAddVideo(); }}
              className="flex items-center justify-center w-5 h-5 text-[var(--theme-mode-text-muted)] hover:text-[var(--theme-primary)]"
            >
              <UI_AddVideoIcon size={15} />
            </button>
          )}
        </div>

        <div className="flex-1 flex flex-col gap-2">
          <div className="flex items-start">
            <div className="flex-1 gap-1 min-w-0">
              <UI_SwimmerNameCell
                firstName={res.first_name}
                lastName={res.last_name}
                club={res.club}
                clubId={res.club_id}
                isRelay={res.is_relay}
                relaySwimmersList={res.relay_swimmers}
                relaySwimmersName={res.relay_swimmers_name}
                onClick={handleNameClick}
                firstLineClassName="text-xl font-bold truncate1 min-w-0 text-[var(--theme-mode-text)]"
                secondLineClassName="text-sm mt-0.5 text-[var(--theme-mode-text-secondary)]"
                showClubIcon={false}
                isRecordHolder={isRecordHolder}
          recordKind={recordHolderMark?.kind ?? (isMastersResult ? 'masters' : 'age')}
          recordScope={recordHolderMark?.scope ?? undefined}
              />
              {showDate && (
                <ResultRowDateInfo date={res.date} competition={res.competition} showCompetition={showCompetition} />
              )}
              <UI_SwimmerGallery gallery={res.gallery} />
            </div>

            {/* Клуб — колонкой между именем и временем.
                Размер — обёрткой: динамические w-N в UI_ClubIcon Tailwind не генерирует. */}
            {showClub && (
              <div className="flex shrink-0 items-start justify-center px-1 pt-0.5">
                <div style={{ width: 28, height: 28 }}>
                  <UI_ClubIcon clubName={res.club} clubId={res.club_id} iconWidth="full" styleType="icon-notext" />
                </div>
              </div>
            )}

            <div className="flex flex-col items-center text-right gap-1 basis-1/3">
              <UI_SwimmerTimeCell
                time={res.time}
                time_split={res.time_split}
                time_fail={res.time_fail}
                time_fail_note={res.time_fail_note}
                firstLineClassName="text-lg font-bold tabular-nums"
                secondLineClassName="text-xs justify-center"
                className="text-right"
                isRecordHolder={isRecordTime}
          recordKind={recordTimeMark?.kind ?? (isMastersResult ? 'masters' : 'age')}
          recordScope={recordTimeMark?.scope ?? undefined}
          quality={res.suspect_reason ? { kind: 'protocol', reason: res.suspect_reason } : null}
          qualityMarker="chip"
              />
              {isSeasonBestTime && <UI_SeasonBestBadge className="mt-0.5" />}
              {showEvent && (
                <UI_SwimmStyleIcon
                  styleName={res.event_style_name}
                  styleLen={res.event_style_len}
                  styleType="icon-len"
                  className="font-bold text-sm max-w-[72px]"
                />
              )}
              {showPoolType && (
                <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} iconWidth="32" labelClassName="text-xs" />
              )}
            </div>

            <div className="shrink-0 self-center pl-1 text-[var(--theme-mode-text-muted)]">
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
                strokeLinecap="round"
                strokeLinejoin="round"
                className={`transition-transform ${isExpanded ? 'rotate-180' : ''}`}
              >
                <path d="M6 9l6 6 6-6" />
              </svg>
            </div>
          </div>

          {isExpanded && (
            <div className="w-full flex items-start gap-3 mb-2 pt-3">
              <div className="flex-1 flex flex-col items-start gap-0.5 min-w-0">
                {hasInternationalPoints && (
                  <div className="text-sm">
                    <span className="text-[var(--theme-mode-text-muted)]">Points: </span>
                    <span className="text-[var(--theme-mode-text)]">{res.international_points ?? ''}</span>
                  </div>
                )}
              </div>

              {/* Спейсеры повторяют ширину колонки клуба и шеврона из верхней строки,
                  чтобы уровень оказался по центру ровно под временем. */}
              {showClub && <div className="shrink-0" style={{ width: 36 }} />}

              <div className="flex justify-center basis-1/3">
                <div style={{ transform: 'scale(0.7)', marginLeft: 18 }}>
                  <UI_NormativeLevelIcon
                    levelName={levelInfo.currentLevel}
                    styleType="gauge"
                    styleSize="size-1"
                    styleName={res.event_style_name}
                    styleLen={res.event_style_len}
                    poolType={res.pool_type}
                    isMasters={isMastersResult}
                    normativeAgeGroup={levelInfo.normativeAgeGroup}
                    progressPercent={levelInfo.progressToNextLevel}
                    nextTime={levelInfo.nextTime}
                  />
                </div>
              </div>

              <div className="shrink-0" style={{ width: 18 }} />
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default ResultsTableMobile;
