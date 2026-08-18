import React from 'react';
import { useAppSelector } from '../../../store/store';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_PrelimLabel from '../../components/mix/prelim-label/prelim-label';
import { Result } from '../../../utils/interfaces/results';

interface ResultsFilteredInfoProps {
  firstResult: Result | undefined;
  showDate: boolean;
  showClub: boolean;
  showAge: boolean;
  showPoolType: boolean;
  showEvent: boolean;
}

// Стили под дизайн-прототип (filter bar)
const LABEL =
  'text-[11px] font-bold uppercase tracking-[0.8px] text-[var(--theme-mode-text-muted)]';
const ALL_VAL = 'text-[20px] font-semibold text-[var(--theme-primary)] leading-none';
const COL = 'flex-1 flex flex-col items-center gap-2 px-2 min-w-0';
const DIV = 'border-r border-[var(--theme-mode-border)]';

function ResultsFilteredInfo({
  firstResult,
  showDate,
  showClub,
  showAge,
  showPoolType,
  showEvent,
}: ResultsFilteredInfoProps) {
  // Индикатор тумблера [prelim] (фильтр Date) под датой: зелёный ON / оранжевый OFF.
  // Показывается только если в данных вообще есть предварительные заплывы — на обычных
  // соревнованиях без прелимов пометка была бы шумом.
  const showPrelims = useAppSelector((state) => !!state.filterSelected?.show_prelims);
  const hasPrelims = useAppSelector((state) =>
    (state.dataSourceSelected?.results ?? []).some((r) => r.heat_type === 'prelim'));
  return (
    <div className="show-filtered-data mb-4">
      <div className="flex items-stretch bg-[var(--theme-mode-surface)] rounded-[14px] shadow-sm px-2 py-4">
        {/* Date */}
        <div className={`${COL} ${DIV}`}>
          <span className={LABEL}>Date</span>
          {showDate ? (
            <>
              <span className={ALL_VAL}>All</span>
              {hasPrelims && <UI_PrelimLabel state={showPrelims ? 'on' : 'off'} className="text-[10px]" />}
            </>
          ) : (
            firstResult?.date && (
              <UI_DateIcon
                paddingClass="px-1 py-1"
                className="text-xs"
                styleType="cube"
                date={firstResult.date}
                prelimState={hasPrelims ? (showPrelims ? 'on' : 'off') : undefined}
              />
            )
          )}
        </div>

        {/* Club */}
        <div className={`${COL} ${DIV}`}>
          <span className={LABEL}>Club</span>
          {showClub ? (
            <span className={ALL_VAL}>All</span>
          ) : (
            firstResult?.club && (
              <UI_ClubIcon clubName={firstResult.club} className="text-xs" iconWidth="10" styleType="icon-text-bottom" />
            )
          )}
        </div>

        {/* Event */}
        <div className={`${COL} ${DIV}`}>
          <span className={LABEL}>Event</span>
          {showEvent ? (
            <span className={ALL_VAL}>All</span>
          ) : (
            firstResult?.event_style_name && (
              <div className="w-[72px] [&_img]:w-full [&_img]:h-auto">
                <UI_SwimmStyleIcon
                  styleName={firstResult.event_style_name}
                  styleLen={firstResult.event_style_len}
                  styleType="icon-len"
                  className="font-bold text-base"
                />
              </div>
            )
          )}
        </div>

        {/* Age */}
        <div className={`${COL} ${DIV}`}>
          <span className={LABEL}>Age</span>
          {showAge ? (
            <span className={ALL_VAL}>All</span>
          ) : (
            firstResult?.event_style_age && (
              <span className="text-2xl font-bold text-[var(--theme-mode-text)] leading-none">{firstResult.event_style_age}</span>
            )
          )}
        </div>

        {/* Pool */}
        <div className={COL}>
          <span className={LABEL}>Pool</span>
          {showPoolType ? (
            <>
              <span className="text-[18px] font-bold text-[var(--theme-mode-text)] leading-none">All</span>
              <span className="text-base text-[var(--theme-primary)] leading-none">〰️</span>
            </>
          ) : (
            firstResult?.pool_type && (
              <UI_PoolIcon styleType="icon-text-top" label={firstResult.pool_type} iconWidth="40" labelClassName="text-sm" />
            )
          )}
        </div>
      </div>
    </div>
  );
}

export default ResultsFilteredInfo;
