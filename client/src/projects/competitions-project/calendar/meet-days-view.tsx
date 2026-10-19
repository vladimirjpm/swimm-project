import React from 'react';
import CalendarLegend from './calendar-legend';
import { meetInMonth } from './calendar-model';
import type { CalendarMeet, CalendarMonth } from './calendar-types';
import MeetCube from './meet-cube';

/*
 * Вид Meet days: все meets месяца (после фильтра по типу) кубами, по дате начала и имени —
 * в порядке ответа API. Правило «meet в месяце» общее с остальными видами. Мобильную ленту
 * дней над кубами добавит Б6.
 */
function MeetDaysView({ month, meets, today }: { month: CalendarMonth; meets: CalendarMeet[]; today: string }) {
  const inMonth = meets
    .filter((meet) => meetInMonth(meet, month))
    .sort((a, b) => a.start.localeCompare(b.start) || a.name.localeCompare(b.name));

  return (
    <div>
      {inMonth.length === 0 ? (
        <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets this month</p>
      ) : (
        <div className="grid grid-cols-1 gap-[10px] sm:grid-cols-2 lg:grid-cols-4">
          {inMonth.map((meet) => (
            <MeetCube key={meet.key} meet={meet} today={today} />
          ))}
        </div>
      )}
      <CalendarLegend />
    </div>
  );
}

export default MeetDaysView;
