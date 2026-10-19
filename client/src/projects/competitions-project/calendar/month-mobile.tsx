import React from 'react';
import { IconCaretLeft, IconCaretRight } from './calendar-icons';
import { addMonths, dayAriaLabel, meetsOnDay, monthGrid, monthKey, weekIndexOf, weekLabel } from './calendar-model';
import type { CalendarMeet, CalendarMonth } from './calendar-types';
import DayMarks from './day-marks';

/*
 * Full month на телефоне (< lg): по умолчанию ОДНА неделя (карточка с шапкой «Week · 18–24 Oct»),
 * по тапу на подпись месяца в строке управления — весь месяц теми же ячейками. Неделя по
 * умолчанию — та, где выбранный день (иначе «сегодня», иначе первая). Ячейка — число и до 4
 * меток (`DayMarks`).
 */

const LETTERS = ['S', 'M', 'T', 'W', 'T', 'F', 'S'];

function Cell({
  iso,
  meets,
  today,
  selected,
  onSelect,
}: {
  iso: string;
  meets: CalendarMeet[];
  today: string;
  selected: boolean;
  onSelect: (iso: string) => void;
}) {
  const isToday = iso === today;
  const champ = meets.some((m) => m.is_championship);

  // Рамка 1px у всех ячеек (прозрачная по умолчанию), чтобы выбор/сегодня не двигали вёрстку.
  const border = isToday
    ? 'border-[var(--t-accent)]'
    : champ
      ? 'border-[var(--t-champ-strip-border)]'
      : 'border-transparent';
  const bg = selected
    ? 'bg-[var(--t-accent-soft)]'
    : champ
      ? 'bg-[var(--t-champ-strip-bg)]'
      : 'bg-transparent';

  return (
    <button
      type="button"
      aria-label={dayAriaLabel(iso, meets.length)}
      aria-pressed={selected}
      onClick={() => onSelect(iso)}
      className={`cc-focus flex h-[52px] cursor-pointer flex-col items-center justify-center gap-1 rounded-[8px] border p-0 ${border} ${bg} ${
        iso < today ? 'opacity-60' : ''
      }`}
    >
      <span className={`text-[12px] ${isToday ? 'font-bold text-[var(--t-accent-text)]' : 'font-medium text-[var(--t-text)]'}`}>
        {Number(iso.slice(8))}
      </span>
      <DayMarks meets={meets} />
    </button>
  );
}

function MonthMobile({
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
  expanded: boolean;
  /** На первой/последней неделе месяца ‹ › ведут в соседний месяц — как ‹ › строки управления. */
  onPrevMonth: () => void;
  onNextMonth: () => void;
  prevDisabled: boolean;
  nextDisabled: boolean;
}) {
  const weeks = React.useMemo(() => monthGrid(month), [month]);

  // Ручной выбор недели живёт вместе с месяцем: смена месяца сбрасывает его сама.
  const [manual, setManual] = React.useState<{ month: string; index: number } | null>(null);
  const id = monthKey(month);
  const selIdx = weekIndexOf(weeks, selectedDay);
  const todayIdx = weekIndexOf(weeks, today);
  const derived = selIdx >= 0 ? selIdx : todayIdx >= 0 ? todayIdx : 0;
  // index -1 — «последняя неделя»: так ‹ с первой недели открывает конец предыдущего месяца.
  const index = manual && manual.month === id
    ? (manual.index < 0 ? weeks.length - 1 : manual.index)
    : derived;

  // Через границу месяца — соседняя неделя по времени (первая / последняя), а не неделя «по правилу».
  const goPrev = () => {
    if (index > 0) { setManual({ month: id, index: index - 1 }); return; }
    setManual({ month: monthKey(addMonths(month, -1)), index: -1 });
    onPrevMonth();
  };
  const goNext = () => {
    if (index < weeks.length - 1) { setManual({ month: id, index: index + 1 }); return; }
    setManual({ month: monthKey(addMonths(month, 1)), index: 0 });
    onNextMonth();
  };
  const prevOff = index === 0 && prevDisabled;
  const nextOff = index === weeks.length - 1 && nextDisabled;

  const visible = expanded ? weeks : [weeks[index]];

  return (
    <div className="flex flex-col gap-[6px] rounded-[14px] bg-[var(--t-surface)] p-2 shadow-[var(--t-shadow)] backdrop-blur-[14px]">
      {!expanded && (
        <div className="flex items-center justify-between text-[12px] text-[var(--t-text-2)]">
          <button
            type="button"
            aria-label="Previous week"
            aria-disabled={prevOff || undefined}
            onClick={() => !prevOff && goPrev()}
            className={`cc-focus flex h-6 w-6 items-center justify-center rounded-[6px] border-0 bg-transparent p-0 text-[var(--t-accent-text)] ${
              prevOff ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'
            }`}
          >
            <IconCaretLeft size={16} />
          </button>
          <span>{`Week · ${weekLabel(weeks[index])}`}</span>
          <button
            type="button"
            aria-label="Next week"
            aria-disabled={nextOff || undefined}
            onClick={() => !nextOff && goNext()}
            className={`cc-focus flex h-6 w-6 items-center justify-center rounded-[6px] border-0 bg-transparent p-0 text-[var(--t-accent-text)] ${
              nextOff ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'
            }`}
          >
            <IconCaretRight size={16} />
          </button>
        </div>
      )}

      <div className="grid grid-cols-7 gap-1" aria-hidden="true">
        {LETTERS.map((l, i) => (
          <span key={i} className="text-center text-[10px] uppercase text-[var(--t-text-2)]">
            {l}
          </span>
        ))}
      </div>

      <div className="grid grid-cols-7 gap-1">
        {visible.flatMap((week, wi) =>
          week.map((iso, col) =>
            iso ? (
              <Cell
                key={iso}
                iso={iso}
                meets={meetsOnDay(meets, iso)}
                today={today}
                selected={iso === selectedDay}
                onSelect={onSelectDay}
              />
            ) : (
              <div key={`blank-${wi}-${col}`} aria-hidden="true" className="h-[52px]" />
            ),
          ),
        )}
      </div>
    </div>
  );
}

export default MonthMobile;
