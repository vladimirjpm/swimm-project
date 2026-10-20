import React from 'react';
import CalendarSidebar from './calendar-sidebar';
import { CalendarError, CalendarLoading } from './calendar-status';
import MonthAgenda from './month-agenda';
import type { CalendarController } from './use-calendar-controller';
import YearView from './year-view';

/*
 * Раскладка календаря на десктопе (≥ 1024, лейаут 9a хендоффа competitions_home-3): слева липкая
 * карточка `CalendarSidebar` (260px, с 1280 — 288px), справа тело вида — Month: `MonthAgenda`
 * (по неделям / день), Year: `YearView`. Большой сетки месяца и Meet days на десктопе больше нет.
 */

function CalendarDesktop({ cal }: { cal: CalendarController }) {
  let body: React.ReactNode;
  if (cal.error) body = <CalendarError onRetry={cal.retry} />;
  else if (!cal.data) body = <CalendarLoading />;
  else if (cal.view === 'year') {
    body = <YearView season={cal.season} meets={cal.seasonMeets} today={cal.today} onPickMonth={cal.openMonth} />;
  } else {
    body = (
      <MonthAgenda
        month={cal.month}
        meets={cal.monthMeets}
        today={cal.today}
        selectedDay={cal.selectedDay}
        onWholeMonth={() => cal.selectDay(null)}
      />
    );
  }

  return (
    <div className="grid grid-cols-[260px_minmax(0,1fr)] items-start gap-6 xl:grid-cols-[288px_minmax(0,1fr)]">
      <CalendarSidebar cal={cal} />
      <div className="min-w-0">{body}</div>
    </div>
  );
}

export default CalendarDesktop;
