import React from 'react';
import './competition-calendar.css';
import CalendarDesktop from './calendar-desktop';
import { useIsDesktop } from './calendar-layout-hooks';
import CalendarMobile from './calendar-mobile';
import { useCalendarController } from './use-calendar-controller';

/*
 * Календарь сезона на /competitions — точка сборки: контроллер (состояние, данные, query) +
 * раскладка по ширине. Десктоп ≥ 1024 — лейаут 9a (мини-месяц слева, кубы по неделям справа),
 * меньше — телефонная раскладка. Обе рисуют один и тот же `CalendarController`.
 *
 * Карта блоков (каждый — свой файл, данные только через пропсы, о соседях не знает):
 *   данные и правила   use-calendar-controller (состояние + query), use-competition-calendar (API),
 *                      calendar-model (чистые функции), calendar-state (query), calendar-demo
 *   раскладки          calendar-desktop (9a: сайдбар + тело), calendar-mobile (строка управления + тело)
 *   управление         calendar-sidebar (липкая карточка), view-segment, period-nav, type-filter
 *   дни                day-grid (мини-месяц / неделя телефона), day-marks, month-mobile, calendar-legend
 *   тела видов         month-agenda (по неделям / день), year-view, meet-days-view, calendar-status
 *   соревнование       cube-grid → meet-cube → group-badge
 */
function CompetitionCalendar() {
  const desktop = useIsDesktop();
  const cal = useCalendarController({ desktop });

  return (
    <section className="cc-root relative" aria-label="Competition calendar" aria-busy={!cal.data && !cal.error}>
      {cal.demoOn && (
        <p className="mb-3 text-[11px] font-bold uppercase tracking-[.08em] text-[var(--t-warn)]">DEMO DATA</p>
      )}
      {desktop ? <CalendarDesktop cal={cal} /> : <CalendarMobile cal={cal} />}
    </section>
  );
}

export default CompetitionCalendar;
