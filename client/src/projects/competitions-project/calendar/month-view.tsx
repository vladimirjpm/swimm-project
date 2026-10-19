import React from 'react';
import { IconTrophy } from './calendar-icons';
import CalendarLegend from './calendar-legend';
import { champMark, chipShape, dayAriaLabel, meetsOnDay, monthGrid } from './calendar-model';
import type { CalendarMeet, CalendarMonth } from './calendar-types';
import GroupBadge from './group-badge';
import MeetCube from './meet-cube';
import MonthMobile from './month-mobile';

/*
 * Вид Full month: сетка месяца с плашками meets (десктоп, ≥ lg) или неделя/месяц с метками
 * (телефон, `MonthMobile`), под ними общие легенда и кубы выбранного дня.
 * `meets` — meets месяца уже после фильтра по типу.
 */

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

// Перелив плашки многодневки на соседнюю ячейку: padding ячейки (7px) + половина gap сетки
// (2px) — иначе между плашками соседних дней остаётся щель. На краю недели (сб справа,
// вс слева) тянем только до границы ячейки: дальше сетки нет.
const CELL_PAD = 7;
const GAP_HALF = 2;

// Высота ячейки — по экрану, чтобы месяц влезал целиком (Б12, ТЗ «Layout /competitions»):
// clamp(76, (высота окна − верх сетки) / 6 − 6, 112). Верх сетки меряем, а не берём константой
// ~300px: hero, строка управления и шапка дней меняются от ширины. Делим на 6 даже у месяца из
// 5 недель — остаток уходит под легенду. Плашек — сколько физически влезает (`fitChips`), а не
// формулой ТЗ floor((h − 24) / 24): та при h=100 даёт 3, а три в 100px не помещаются, и плашка
// с бейджем группы выше обычной.
const CELL_H_MIN = 76;
const CELL_H_MAX = 112;

// Раскладка ячейки: паддинг 2×5 + число 16; плашка 22 (с бейджем группы — 29 + 2×2) + зазор 3;
// строка «+N more» 14 + зазор 3.
const CELL_FIXED = 26;
const CHIP_COST = 25;
const BADGE_CHIP_COST = 36;
const MORE_COST = 17;

/** Сколько плашек влезает в ячейку высоты h; не влезли все — остаётся место под «+N more». */
function fitChips(meets: CalendarMeet[], h: number): CalendarMeet[] {
  let budget = h - CELL_FIXED;
  const shown: CalendarMeet[] = [];
  for (let i = 0; i < meets.length; i++) {
    const cost = champMark(meets[i])?.kind === 'badge' ? BADGE_CHIP_COST : CHIP_COST;
    const needMore = i < meets.length - 1 ? MORE_COST : 0;
    if (cost + needMore > budget) break;
    shown.push(meets[i]);
    budget -= cost;
  }
  return shown;
}

function useCellHeight(gridRef: React.RefObject<HTMLDivElement>): number {
  const [h, setH] = React.useState(CELL_H_MAX);
  React.useLayoutEffect(() => {
    const measure = () => {
      const el = gridRef.current;
      if (!el || el.offsetParent === null) return; // телефон: сетка скрыта
      const top = el.getBoundingClientRect().top + window.scrollY;
      const next = Math.round(Math.min(CELL_H_MAX, Math.max(CELL_H_MIN, (window.innerHeight - top) / 6 - 6)));
      setH((prev) => (prev === next ? prev : next));
    };
    measure();
    window.addEventListener('resize', measure);
    return () => window.removeEventListener('resize', measure);
  }, [gridRef]);
  return h;
}

function MeetChip({ meet, iso, col }: { meet: CalendarMeet; iso: string; col: number }) {
  const { first, joinLeft, joinRight, dayNo } = chipShape(meet, iso);
  const mark = meet.is_championship ? champMark(meet) : null;

  const overL = joinLeft ? (col === 0 ? CELL_PAD : CELL_PAD + GAP_HALF) : 0;
  const overR = joinRight ? (col === 6 ? CELL_PAD : CELL_PAD + GAP_HALF) : 0;
  const baseL = mark ? 2 : 6;
  const padY = mark?.kind === 'badge' ? 2 : 0;

  return (
    <span
      title={meet.name}
      className={`flex min-h-[22px] shrink-0 items-center gap-[5px] overflow-hidden whitespace-nowrap text-[12px] leading-none ${
        mark ? 'bg-[var(--t-champ-chip-bg)] text-[var(--t-champ-ink)]' : 'bg-[var(--t-nested)] text-[var(--t-text)]'
      }`}
      style={{
        // Высота 22px (хендофф competitions_home-2) — без вертикального паддинга; плашку с бейджем
        // группы (29px) держит сам бейдж, ей оставлен зазор 2px.
        padding: `${padY}px ${6 + overR}px ${padY}px ${baseL + overL}px`,
        marginLeft: -overL,
        marginRight: -overR,
        borderRadius: `${joinLeft ? 0 : 4}px ${joinRight ? 0 : 4}px ${joinRight ? 0 : 4}px ${joinLeft ? 0 : 4}px`,
      }}
    >
      {mark?.kind === 'badge' && <GroupBadge group={mark.group} size="cell" />}
      {mark?.kind === 'text' && <IconTrophy size={11} className="shrink-0" />}
      {first ? (
        <span dir="auto" className="min-w-0 flex-1 truncate text-left">
          {meet.name}
        </span>
      ) : (
        <span className="min-w-0 flex-1 truncate">{`↳ Day ${dayNo}`}</span>
      )}
    </span>
  );
}

function DayCell({
  iso,
  col,
  meets,
  today,
  selected,
  onSelect,
  height,
}: {
  iso: string;
  col: number;
  meets: CalendarMeet[];
  today: string;
  selected: boolean;
  onSelect: (iso: string) => void;
  height: number;
}) {
  const isToday = iso === today;
  const past = iso < today;
  const shown = fitChips(meets, height);

  // Обводка «сегодня» слабее выбранного дня; сегодня + выбран — как выбранный.
  const outline = selected
    ? { outline: '2px solid var(--t-accent)', outlineOffset: '-2px' }
    : isToday
      ? { outline: '1.5px solid var(--t-accent)', outlineOffset: '-1.5px' }
      : undefined;

  // Прошедшее — opacity .88, а не .6 из макета: при .6 золотая плашка чемпионата давала 3.0
  // (светлая) / 3.6 (тёмная), а 9px подписи ленты телефона в тёмной держат 4.5 только с .88 —
  // замер Б9. Одно значение на все виды: месяцы Year, ячейки и тайлы телефона.
  return (
    <button
      type="button"
      aria-label={dayAriaLabel(iso, meets.length)}
      aria-pressed={selected}
      onClick={() => onSelect(iso)}
      style={{ ...outline, height }}
      className={`cc-focus-ring flex cursor-pointer flex-col gap-[3px] overflow-hidden rounded-[8px] px-[7px] py-[5px] text-left shadow-[var(--t-shadow)] backdrop-blur-[14px] hover:bg-[var(--t-nested)] ${
        selected ? 'bg-[var(--t-nested)]' : 'bg-[var(--t-surface)]'
      } ${past ? 'opacity-[.88]' : ''}`}
    >
      <span
        className={`text-[12px] leading-4 ${
          isToday ? 'font-bold text-[var(--t-accent-text)]' : 'font-medium text-[var(--t-text)]'
        }`}
      >
        {Number(iso.slice(8))}
      </span>
      {shown.map((meet) => (
        <MeetChip key={meet.key} meet={meet} iso={iso} col={col} />
      ))}
      {meets.length > shown.length && (
        <span className="text-[11px] leading-[14px] text-[var(--t-accent-text)]">{`+${meets.length - shown.length} more`}</span>
      )}
    </button>
  );
}

function MonthView({
  month,
  meets,
  today,
  selectedDay,
  onSelectDay,
  expanded,
  onPrevMonth,
  onNextMonth,
  prevDisabled,
  nextDisabled,
}: {
  month: CalendarMonth;
  meets: CalendarMeet[];
  today: string;
  selectedDay: string | null;
  onSelectDay: (iso: string) => void;
  /** Телефон: месяц развёрнут целиком. */
  expanded: boolean;
  onPrevMonth: () => void;
  onNextMonth: () => void;
  prevDisabled: boolean;
  nextDisabled: boolean;
}) {
  const weeks = React.useMemo(() => monthGrid(month), [month]);
  const dayMeets = selectedDay ? meetsOnDay(meets, selectedDay) : [];
  const gridRef = React.useRef<HTMLDivElement>(null);
  const cellH = useCellHeight(gridRef);

  // Клик по дню (десктоп) → плавный скролл к кубам дня: месяц теперь занимает экран, и кубы
  // ниже края (Б12). Скроллим после перерисовки — кубы нового дня уже на месте. Шапка сайта
  // липкая, её высоту вычитаем.
  const cubesRef = React.useRef<HTMLDivElement>(null);
  const [scrollRequest, setScrollRequest] = React.useState(0);
  const pickDay = (iso: string) => {
    onSelectDay(iso);
    setScrollRequest((n) => n + 1);
  };
  React.useEffect(() => {
    const el = cubesRef.current;
    if (!scrollRequest || !el) return;
    const topbar = document.querySelector<HTMLElement>('[data-app-topbar]')?.offsetHeight ?? 0;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    window.scrollTo({
      top: el.getBoundingClientRect().top + window.scrollY - topbar - 12,
      behavior: reduced ? 'auto' : 'smooth',
    });
  }, [scrollRequest]);

  return (
    <div>
      <div className="lg:hidden">
        <MonthMobile
          month={month}
          meets={meets}
          today={today}
          selectedDay={selectedDay}
          onSelectDay={onSelectDay}
          expanded={expanded}
          onPrevMonth={onPrevMonth}
          onNextMonth={onNextMonth}
          prevDisabled={prevDisabled}
          nextDisabled={nextDisabled}
        />
      </div>
      <div className="hidden lg:block">
      <div className="mb-[2px] grid grid-cols-7 gap-1">
        {WEEKDAYS.map((wd) => (
          <span key={wd} className="px-2 pb-[6px] text-[11px] uppercase tracking-[.08em] text-[var(--t-text-2)]">
            {wd}
          </span>
        ))}
      </div>
      <div ref={gridRef} className="grid grid-cols-7 gap-1">
        {weeks.flatMap((week, wi) =>
          week.map((iso, col) =>
            iso ? (
              <DayCell
                key={iso}
                iso={iso}
                col={col}
                meets={meetsOnDay(meets, iso)}
                today={today}
                selected={iso === selectedDay}
                onSelect={pickDay}
                height={cellH}
              />
            ) : (
              <div key={`blank-${wi}-${col}`} aria-hidden="true" style={{ height: cellH }} />
            ),
          ),
        )}
      </div>
      </div>

      <CalendarLegend />

      {meets.length === 0 && <p className="mt-4 text-[13px] text-[var(--t-text-2)]">No meets this month</p>}

      <div ref={cubesRef} aria-live="polite" className="mt-4">
        {meets.length > 0 && selectedDay && dayMeets.length === 0 && (
          <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets on this day.</p>
        )}
        {dayMeets.length > 0 && (
          <div className="grid grid-cols-1 gap-[10px] sm:grid-cols-2 lg:grid-cols-4">
            {dayMeets.map((meet) => (
              <MeetCube key={meet.key} meet={meet} today={today} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default MonthView;
