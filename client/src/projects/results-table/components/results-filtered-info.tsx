import React from 'react';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_PrelimLabel from '../../components/mix/prelim-label/prelim-label';
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
 * Полоса выбранных фильтров (design_handoff_position_filter).
 *
 * Правила хендоффа:
 *  - колонка в состоянии «All» не занимает полноценное место (десктоп — узкая, мобайл — мелкий чип);
 *  - Position — шестой фильтр; в состоянии `all` на десктопе колонка не рендерится вовсе;
 *  - мобайл (< 768px) — две строки: сверху мелко всё «All», снизу крупно и по центру выбранное;
 *    пустая нижняя строка не рендерится вообще.
 *
 * Кликабелен только чип Position (значений три, попап не нужен — клик прокручивает их по кругу).
 * Остальные чипы остаются некликабельными, как и раньше: их выбор живёт в сайдбаре.
 */

// ——— десктоп
const D_LABEL =
  'text-[11px] font-extrabold uppercase tracking-[0.8px] text-[var(--theme-mode-text-muted)]';
// Все колонки одинаковые и растянуты симметрично: flex-1 basis-0 + одинаковый padding,
// поэтому ширина делится поровну независимо от того, что внутри (значение или «All»).
const D_COL = 'flex-1 basis-0 min-w-0 flex flex-col items-center justify-start gap-2 px-3 py-1.5';
const D_IDLE_VAL = 'text-[20px] font-extrabold text-[var(--theme-mode-text-muted)] leading-none';
const D_DIV = 'border-r border-[var(--theme-mode-border)]';

// ——— мобайл
const M_IDLE_CHIP =
  'flex-1 min-w-0 flex flex-col items-center gap-0.5 px-0.5 py-[5px] rounded-lg ' +
  'bg-[var(--theme-mode-surface-alt)] border border-[var(--theme-mode-border)]';
const M_IDLE_LABEL =
  'text-[8.5px] font-extrabold uppercase tracking-[0.3px] text-[var(--theme-mode-text-muted)] ' +
  'whitespace-nowrap overflow-hidden text-ellipsis max-w-full';
const M_IDLE_VAL =
  'text-[10.5px] font-bold text-[var(--theme-mode-text-muted)] leading-[1.2] ' +
  'whitespace-nowrap overflow-hidden text-ellipsis max-w-full';
const M_ACTIVE_CHIP = 'flex flex-col items-center justify-center gap-1 px-3.5 py-[7px] rounded-[10px]';
const M_ACTIVE_LABEL =
  'text-[9.5px] font-extrabold uppercase tracking-[0.6px] text-[var(--theme-mode-text-muted)] whitespace-nowrap';

/** Тон активного чипа: синий (обычный фильтр / Top N) или золотой (podium). */
const activeToneClass = (gold: boolean) =>
  gold
    ? 'bg-[var(--theme-personal-badge-bg)] border border-[var(--theme-personal-border)]'
    : 'bg-[color-mix(in_srgb,var(--theme-primary)_8%,var(--theme-mode-surface))] ' +
      'border border-[color-mix(in_srgb,var(--theme-primary)_25%,transparent)]';

interface ChipItem {
  key: string;
  label: string; // полная подпись (десктоп, нижняя строка мобайла)
  shortLabel: string; // сокращённая (верхняя строка мобайла)
  active: boolean;
  gold?: boolean;
  desktopValue: React.ReactNode;
  mobileValue: React.ReactNode;
  onClick?: () => void;
}

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

  const items: ChipItem[] = [
    {
      key: 'date',
      label: 'Date',
      shortLabel: 'Date',
      active: !showDate && !!firstResult?.date,
      desktopValue: firstResult?.date && (
        <UI_DateIcon
          paddingClass="px-1 py-1"
          className="text-xs"
          styleType="cube"
          date={firstResult.date}
          prelimState={hasPrelims ? (showPrelims ? 'on' : 'off') : undefined}
        />
      ),
      mobileValue: firstResult?.date && (
        <UI_DateIcon
          paddingClass="px-1 py-0.5"
          className="text-[10px]"
          styleType="cube"
          date={firstResult.date}
          prelimState={hasPrelims ? (showPrelims ? 'on' : 'off') : undefined}
        />
      ),
    },
    {
      key: 'club',
      label: 'Club',
      shortLabel: 'Club',
      active: !showClub && !!firstResult?.club,
      desktopValue: firstResult?.club && (
        <UI_ClubIcon clubName={firstResult.club} clubId={firstResult.club_id} className="text-xs" iconWidth="10" styleType="icon-text-bottom" />
      ),
      mobileValue: firstResult?.club && (
        <UI_ClubIcon clubName={firstResult.club} clubId={firstResult.club_id} className="text-[10px]" iconWidth="8" styleType="icon-text-bottom" />
      ),
    },
    {
      key: 'event',
      label: 'Event',
      shortLabel: 'Event',
      active: !showEvent && !!firstResult?.event_style_name,
      desktopValue: firstResult?.event_style_name && (
        <div className="w-[72px] [&_img]:w-full [&_img]:h-auto">
          <UI_SwimmStyleIcon
            styleName={firstResult.event_style_name}
            styleLen={firstResult.event_style_len}
            styleType="icon-len"
            className="font-bold text-base"
          />
        </div>
      ),
      mobileValue: firstResult?.event_style_name && (
        <div className="w-[52px] [&_img]:w-full [&_img]:h-auto">
          <UI_SwimmStyleIcon
            styleName={firstResult.event_style_name}
            styleLen={firstResult.event_style_len}
            styleType="icon-len"
            className="font-bold text-[13px]"
          />
        </div>
      ),
    },
    {
      key: 'age',
      label: 'Age',
      shortLabel: 'Age',
      active: !showAge && !!firstResult?.event_style_age,
      desktopValue: (
        <span className="text-2xl font-extrabold text-[var(--theme-mode-text)] leading-none">
          {firstResult?.event_style_age}
        </span>
      ),
      mobileValue: (
        <span className="text-[16px] font-extrabold text-[var(--theme-primary)] leading-[1.2]">
          {firstResult?.event_style_age}
        </span>
      ),
    },
    {
      key: 'pool',
      label: 'Pool',
      shortLabel: 'Pool',
      active: !showPoolType && !!firstResult?.pool_type,
      desktopValue: firstResult?.pool_type && (
        <UI_PoolIcon styleType="icon-text-top" label={firstResult.pool_type} iconWidth="40" labelClassName="text-sm" />
      ),
      mobileValue: firstResult?.pool_type && (
        <UI_PoolIcon styleType="icon-text-top" label={firstResult.pool_type} iconWidth="26" labelClassName="text-[11px]" />
      ),
    },
    {
      key: 'position',
      label: 'Position',
      shortLabel: 'Pos',
      active: position !== 'all',
      gold: position === 'podium',
      onClick: cyclePosition,
      desktopValue:
        position === 'podium' ? (
          podiumValue('text-[19px]', 'text-[11px]')
        ) : (
          <span className="text-[19px] font-extrabold text-[var(--theme-primary)] leading-none whitespace-nowrap">
            {positionLabel}
          </span>
        ),
      mobileValue:
        position === 'podium' ? (
          podiumValue('text-[13px]', 'text-[11px]')
        ) : (
          <span className="text-[16px] font-extrabold text-[var(--theme-primary)] leading-[1.2] whitespace-nowrap">
            {positionLabel}
          </span>
        ),
    },
  ];

  const idle = items.filter((i) => !i.active);
  const active = items.filter((i) => i.active);

  // Десктоп: Position в состоянии «all» не рендерится вовсе — в отличие от остальных,
  // у которых «All» означает осмысленное «в выборке все значения».
  const desktopItems = items.filter((i) => i.active || i.key !== 'position');

  return (
    <div className="show-filtered-data mb-4">
      {/* ——— Десктоп (≥ 768px) */}
      <div className="hidden md:flex items-stretch justify-center bg-[var(--theme-mode-surface)] border border-[var(--theme-mode-border)] rounded-[14px] shadow-sm px-2 py-4">
        {desktopItems.map((item, idx) => (
          // Разделитель — на обёртке, а не на самой колонке: так подсветка активного
          // Position (скруглённый фон) не съедает вертикальную линию и не ломает ритм.
          <div
            key={item.key}
            className={`flex-1 basis-0 min-w-0 flex ${idx === desktopItems.length - 1 ? '' : D_DIV}`}
          >
            <div
              onClick={item.onClick}
              className={
                `${D_COL} ` +
                (item.active && item.key === 'position'
                  ? `rounded-[10px] cursor-pointer ${activeToneClass(!!item.gold)}`
                  : item.onClick
                    ? 'cursor-pointer'
                    : '')
              }
            >
              <span className={D_LABEL}>{item.label}</span>
              {item.active ? item.desktopValue : <span className={D_IDLE_VAL}>All</span>}
              {item.key === 'date' && !item.active && hasPrelims && (
                <UI_PrelimLabel state={showPrelims ? 'on' : 'off'} className="text-[10px]" />
              )}
            </div>
          </div>
        ))}
      </div>

      {/* ——— Мобайл (< 768px): две строки */}
      <div className="flex md:hidden flex-col gap-2 bg-[var(--theme-mode-surface)] border border-[var(--theme-mode-border)] rounded-xl shadow-sm p-2">
        {idle.length > 0 && (
          <div className="flex flex-nowrap gap-1">
            {idle.map((item) => (
              <div
                key={item.key}
                onClick={item.onClick}
                className={`${M_IDLE_CHIP}${item.onClick ? ' cursor-pointer' : ''}`}
              >
                <span className={M_IDLE_LABEL}>{item.shortLabel}</span>
                <span className={M_IDLE_VAL}>All</span>
              </div>
            ))}
          </div>
        )}
        {active.length > 0 && (
          <div
            className={
              'flex justify-center flex-wrap gap-2 pt-[9px]' +
              (idle.length > 0 ? ' border-t border-dashed border-[var(--theme-mode-border)]' : '')
            }
          >
            {active.map((item) => (
              <div
                key={item.key}
                onClick={item.onClick}
                className={`${M_ACTIVE_CHIP} ${activeToneClass(!!item.gold)}${item.onClick ? ' cursor-pointer' : ''}`}
              >
                <span className={M_ACTIVE_LABEL}>{item.label}</span>
                {item.mobileValue}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default ResultsFilteredInfo;
