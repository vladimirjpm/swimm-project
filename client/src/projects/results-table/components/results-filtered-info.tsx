import React from 'react';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_PrelimLabel from '../../components/mix/prelim-label/prelim-label';
import FilterBar, { FilterBarChip } from '../../components/filter-section/filter-bar';
import { Result } from '../../../utils/interfaces/results';
import {
  PositionFilterValue,
  getPositionLabel,
  nextPositionValue,
} from '../../../utils/constants/position-filter';

interface ResultsFilteredInfoProps {
  firstResult: Result | undefined;
  showDate: boolean;
  showClub: boolean;
  showAge: boolean;
  showPoolType: boolean;
  showEvent: boolean;
}

/**
 * Полоса выбранных фильтров results (design_handoff_position_filter).
 *
 * Вёрстку полосы рисует ОБЩИЙ `FilterBar` (Ф1 плана `docs/plans/my-media-filters-plan.md`) —
 * тот же, что на `/season-best`. Здесь остаётся только сборка чипов: что показать в колонке
 * Date, Club, Event… Правила хендоффа («All» мельче, две строки на мобайле, пустая строка
 * не рендерится) живут в компоненте, а не здесь.
 *
 * Кликабелен только чип Position (значений три, попап не нужен — клик прокручивает их по
 * кругу). Остальные чипы некликабельны: их выбор живёт в сайдбаре.
 */

function ResultsFilteredInfo({
  firstResult,
  showDate,
  showClub,
  showAge,
  showPoolType,
  showEvent,
}: ResultsFilteredInfoProps) {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  // Индикатор тумблера [prelim] (фильтр Date) под датой: зелёный ON / оранжевый OFF.
  // Показывается только если в данных вообще есть предварительные заплывы — на обычных
  // соревнованиях без прелимов пометка была бы шумом.
  const showPrelims = !!filters?.show_prelims;
  const hasPrelims = useAppSelector((state) =>
    (state.dataSourceSelected?.results ?? [])
      .some((r) => r.heat_type === 'prelim' || r.heat_type === 'extra'));

  // Дефолт position_filter — 'top', а не 'all': фильтр активен почти всегда,
  // потому его и показываем — он единственный, кто урезает выдачу молча.
  const position: PositionFilterValue = filters?.position_filter || 'top';
  const positionLabel = getPositionLabel(position);
  const cyclePosition = () =>
    dispatch(rootActions.updateState({
      filterSelected: { ...filters, position_filter: nextPositionValue(position) },
    }));

  const podiumValue = (medalSize: string, textSize: string) => (
    <span className={`${medalSize} leading-[1.3] tracking-[-0.5px] whitespace-nowrap`}>
      🥇🥈🥉{' '}
      <span className={`${textSize} font-black text-[var(--theme-personal-accent)]`}>1-2-3</span>
    </span>
  );

  const chips: FilterBarChip[] = [
    {
      key: 'date',
      label: 'Date',
      active: !showDate && !!firstResult?.date,
      value: firstResult?.date && (
        <UI_DateIcon
          paddingClass="px-1 py-1"
          className="text-xs"
          styleType="cube"
          date={firstResult.date}
          prelimState={hasPrelims ? (showPrelims ? 'on' : 'off') : undefined}
        />
      ),
      valueCompact: firstResult?.date && (
        <UI_DateIcon
          paddingClass="px-1 py-0.5"
          className="text-[10px]"
          styleType="cube"
          date={firstResult.date}
          prelimState={hasPrelims ? (showPrelims ? 'on' : 'off') : undefined}
        />
      ),
      idleExtra: hasPrelims ? (
        <UI_PrelimLabel state={showPrelims ? 'on' : 'off'} className="text-[10px]" />
      ) : undefined,
    },
    {
      key: 'club',
      label: 'Club',
      active: !showClub && !!firstResult?.club,
      value: firstResult?.club && (
        <UI_ClubIcon clubName={firstResult.club} clubId={firstResult.club_id} className="text-xs" iconWidth="10" styleType="icon-text-bottom" />
      ),
      valueCompact: firstResult?.club && (
        <UI_ClubIcon clubName={firstResult.club} clubId={firstResult.club_id} className="text-[10px]" iconWidth="8" styleType="icon-text-bottom" />
      ),
    },
    {
      key: 'event',
      label: 'Event',
      active: !showEvent && !!firstResult?.event_style_name,
      value: firstResult?.event_style_name && (
        <div className="w-[96px] [&_img]:w-full [&_img]:h-auto">
          <UI_SwimmStyleIcon
            styleName={firstResult.event_style_name}
            styleLen={firstResult.event_style_len}
            styleType="icon-len"
            className="src-results-filtered-info font-bold text-base"
          />
        </div>
      ),
      valueCompact: firstResult?.event_style_name && (
        <div className="w-[52px] [&_img]:w-full [&_img]:h-auto">
          <UI_SwimmStyleIcon
            styleName={firstResult.event_style_name}
            styleLen={firstResult.event_style_len}
            styleType="icon-len"
            className="src-results-filtered-info font-bold text-[13px]"
          />
        </div>
      ),
    },
    {
      key: 'age',
      label: 'Age',
      active: !showAge && !!firstResult?.event_style_age,
      value: (
        <span className="text-2xl font-extrabold text-[var(--theme-mode-text)] leading-none">
          {firstResult?.event_style_age}
        </span>
      ),
      valueCompact: (
        <span className="text-[16px] font-extrabold text-[var(--theme-mode-accent-on-surface)] leading-[1.2]">
          {firstResult?.event_style_age}
        </span>
      ),
    },
    {
      key: 'pool',
      label: 'Pool',
      active: !showPoolType && !!firstResult?.pool_type,
      value: firstResult?.pool_type && (
        <UI_PoolIcon styleType="icon-text-top" label={firstResult.pool_type} iconWidth="40" labelClassName="text-sm" />
      ),
      valueCompact: firstResult?.pool_type && (
        <UI_PoolIcon styleType="icon-text-top" label={firstResult.pool_type} iconWidth="26" labelClassName="text-[11px]" />
      ),
    },
    {
      key: 'position',
      label: 'Position',
      shortLabel: 'Pos',
      active: position !== 'all',
      tone: position === 'podium' ? 'gold' : 'accent',
      onClick: cyclePosition,
      // В состоянии «all» колонки Position на десктопе нет вовсе — в отличие от остальных,
      // у которых «All» означает осмысленное «в выборке все значения».
      hideWhenIdle: true,
      value:
        position === 'podium' ? (
          podiumValue('text-[19px]', 'text-[11px]')
        ) : (
          <span className="text-[19px] font-extrabold text-[var(--theme-mode-accent-on-surface)] leading-none whitespace-nowrap">
            {positionLabel}
          </span>
        ),
      valueCompact:
        position === 'podium' ? (
          podiumValue('text-[13px]', 'text-[11px]')
        ) : (
          <span className="text-[16px] font-extrabold text-[var(--theme-mode-accent-on-surface)] leading-[1.2] whitespace-nowrap">
            {positionLabel}
          </span>
        ),
    },
  ];

  return <FilterBar chips={chips} desktop="columns" rows="card" className="show-filtered-data mb-4" />;
}

export default ResultsFilteredInfo;
