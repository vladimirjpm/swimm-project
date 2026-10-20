import React from 'react';
import {
  dayAriaLabel,
  daysWithMeets,
  meetInMonth,
  meetsOnDay,
  monthKey,
  shortDate,
  weekdayShortOf,
} from './calendar-model';
import type { CalendarMeet, CalendarMonth } from './calendar-types';
import CubeGrid from './cube-grid';
import DayMarks from './day-marks';

/*
 * Вид Meet days (телефон и планшет < lg; на десктопе его роль взял список по неделям 9a):
 * все meets месяца (после фильтра по типу) кубами, по дате начала и имени. Над кубами лента
 * дней месяца, где есть meets: тап по дню оставляет кубы только этого дня, повторный — снова все.
 */

function MeetDaysView({ month, meets, today }: { month: CalendarMonth; meets: CalendarMeet[]; today: string }) {
  // Выбранный день ключуется месяцем: смена месяца сбрасывает фильтр сама.
  const [picked, setPicked] = React.useState<{ month: string; day: string } | null>(null);
  const id = monthKey(month);
  const filterDay = picked && picked.month === id ? picked.day : null;

  const inMonth = meets
    .filter((meet) => meetInMonth(meet, month))
    .sort((a, b) => a.start.localeCompare(b.start) || a.name.localeCompare(b.name));
  const days = daysWithMeets(inMonth, month);
  const shown = filterDay ? meetsOnDay(inMonth, filterDay) : inMonth;

  return (
    <div>
      {days.length > 0 && (
        <>
          <div className="cc-strip -mx-4 -my-1 flex gap-[6px] overflow-x-auto px-4 py-1">
            {days.map((iso) => {
              const dayMeets = meetsOnDay(inMonth, iso);
              const champ = dayMeets.some((m) => m.is_championship);
              const selected = iso === filterDay;
              return (
                <button
                  key={iso}
                  type="button"
                  aria-label={dayAriaLabel(iso, dayMeets.length)}
                  aria-pressed={selected}
                  onClick={() => setPicked(selected ? null : { month: id, day: iso })}
                  className={`cc-focus flex w-[52px] shrink-0 cursor-pointer flex-col items-center gap-[3px] rounded-[8px] border px-0 py-[6px] ${
                    selected
                      ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)]'
                      : champ
                        ? 'border-[var(--t-champ-strip-border)] bg-[var(--t-champ-strip-bg)]'
                        : 'border-[var(--t-border-2)] bg-[var(--t-surface)]'
                  } ${iso < today ? 'opacity-[.88]' : ''}`}
                >
                  <span className="text-[9px] uppercase text-[var(--t-text-2)]">{weekdayShortOf(iso)}</span>
                  <span className="text-[17px] font-medium text-[var(--t-text)]">{Number(iso.slice(8))}</span>
                  <DayMarks meets={dayMeets} />
                </button>
              );
            })}
          </div>
          <p className="mb-3 mt-2 text-[11px] text-[var(--t-text-2)]">
            {filterDay ? `${weekdayShortOf(filterDay)} · ${shortDate(filterDay)} · tap again for all` : 'All meet days'}
          </p>
        </>
      )}

      <div aria-live="polite">
        {inMonth.length === 0 ? (
          <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets this month</p>
        ) : (
          <CubeGrid meets={shown} today={today} />
        )}
      </div>
    </div>
  );
}

export default MeetDaysView;
