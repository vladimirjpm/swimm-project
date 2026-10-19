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

function MeetChip({ meet, iso, col }: { meet: CalendarMeet; iso: string; col: number }) {
  const { first, joinLeft, joinRight, dayNo } = chipShape(meet, iso);
  const mark = meet.is_championship ? champMark(meet) : null;

  const overL = joinLeft ? (col === 0 ? CELL_PAD : CELL_PAD + GAP_HALF) : 0;
  const overR = joinRight ? (col === 6 ? CELL_PAD : CELL_PAD + GAP_HALF) : 0;
  const baseL = mark ? 2 : 6;

  return (
    <span
      title={meet.name}
      className={`flex items-center gap-[5px] overflow-hidden whitespace-nowrap text-[11px] ${
        mark ? 'bg-[var(--t-champ-chip-bg)] text-[var(--t-champ-ink)]' : 'bg-[var(--t-nested)] text-[var(--t-text)]'
      }`}
      style={{
        padding: mark ? `2px ${6 + overR}px 2px ${baseL + overL}px` : `3px ${6 + overR}px 3px ${baseL + overL}px`,
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
}: {
  iso: string;
  col: number;
  meets: CalendarMeet[];
  today: string;
  selected: boolean;
  onSelect: (iso: string) => void;
}) {
  const isToday = iso === today;
  const past = iso < today;
  const shown = meets.slice(0, 3);

  // Обводка «сегодня» слабее выбранного дня; сегодня + выбран — как выбранный.
  const outline = selected
    ? { outline: '2px solid var(--t-accent)', outlineOffset: '-2px' }
    : isToday
      ? { outline: '1.5px solid var(--t-accent)', outlineOffset: '-1.5px' }
      : undefined;

  return (
    <button
      type="button"
      aria-label={dayAriaLabel(iso, meets.length)}
      aria-pressed={selected}
      onClick={() => onSelect(iso)}
      style={outline}
      className={`cc-focus-ring flex min-h-[112px] cursor-pointer flex-col gap-1 rounded-[8px] px-[7px] py-[6px] text-left shadow-[var(--t-shadow)] backdrop-blur-[14px] hover:bg-[var(--t-nested)] ${
        selected ? 'bg-[var(--t-nested)]' : 'bg-[var(--t-surface)]'
      } ${past ? 'opacity-60' : ''}`}
    >
      <span
        className={`text-[12px] ${
          isToday ? 'font-bold text-[var(--t-accent-text)]' : 'font-medium text-[var(--t-text)]'
        }`}
      >
        {Number(iso.slice(8))}
      </span>
      {shown.map((meet) => (
        <MeetChip key={meet.key} meet={meet} iso={iso} col={col} />
      ))}
      {meets.length > 3 && (
        <span className="text-[11px] text-[var(--t-accent-text)]">{`+${meets.length - 3} more`}</span>
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
      <div className="grid grid-cols-7 gap-1">
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
                onSelect={onSelectDay}
              />
            ) : (
              <div key={`blank-${wi}-${col}`} aria-hidden="true" className="min-h-[112px]" />
            ),
          ),
        )}
      </div>
      </div>

      <CalendarLegend />

      {meets.length === 0 && <p className="mt-4 text-[13px] text-[var(--t-text-2)]">No meets this month</p>}

      <div aria-live="polite" className="mt-4">
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
