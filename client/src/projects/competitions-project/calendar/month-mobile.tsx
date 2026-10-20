import React from 'react';
import { addMonths, monthGrid, monthKey, weekIndexOf, weekLabel } from './calendar-model';
import type { CalendarMeet, CalendarMonth } from './calendar-types';
import DayGrid from './day-grid';
import PeriodNav from './period-nav';

/*
 * Full month на телефоне (< lg): по умолчанию ОДНА неделя (карточка с шапкой «‹ Week · 18–24 Oct ›»),
 * по тапу на подпись месяца в строке управления — весь месяц. Неделя по умолчанию — та, где
 * выбранный день (иначе «сегодня», иначе первая). Ячейки — общая `DayGrid`.
 */

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

  return (
    <div className="flex flex-col gap-[6px] rounded-[14px] bg-[var(--t-surface)] p-2 shadow-[var(--t-shadow)] backdrop-blur-[14px]">
      {!expanded && (
        <PeriodNav
          title={`Week · ${weekLabel(weeks[index])}`}
          unit="week"
          size="sm"
          onPrev={goPrev}
          onNext={goNext}
          prevDisabled={index === 0 && prevDisabled}
          nextDisabled={index === weeks.length - 1 && nextDisabled}
          titleClassName="text-[12px] text-[var(--t-text-2)]"
        />
      )}
      <DayGrid
        weeks={expanded ? weeks : [weeks[index]]}
        meets={meets}
        today={today}
        selectedDay={selectedDay}
        onSelectDay={onSelectDay}
      />
    </div>
  );
}

export default MonthMobile;
