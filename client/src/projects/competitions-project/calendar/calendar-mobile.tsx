import React from 'react';
import CalendarLegend from './calendar-legend';
import { IconCaretDown, IconCaretUp, IconFunnel } from './calendar-icons';
import { addMonths, meetsOnDay } from './calendar-model';
import { CalendarError, CalendarLoading } from './calendar-status';
import CubeGrid from './cube-grid';
import MeetDaysView from './meet-days-view';
import MonthMobile from './month-mobile';
import PeriodNav from './period-nav';
import TypeFilter from './type-filter';
import type { CalendarController } from './use-calendar-controller';
import ViewSegment, { type ViewOption } from './view-segment';
import YearView from './year-view';

/*
 * Раскладка календаря на телефоне и планшете (< 1024): строка ‹ период › (в Month подпись —
 * тоггл разворота месяца), сегмент Year · Month · Meet + кнопка Type с плавно раскрывающейся
 * панелью типов; ниже тело вида и легенда. Month — неделя/месяц (`MonthMobile`) и кубы
 * выбранного дня, Meet — лента дней + кубы, Year — карточки месяцев.
 */

const MOBILE_VIEWS: ViewOption[] = [
  { value: 'year', label: 'Year' },
  { value: 'month', label: 'Month' },
  { value: 'meets', label: 'Meet' },
];

function MobileControls({
  cal,
  monthExpanded,
  onToggleMonth,
}: {
  cal: CalendarController;
  monthExpanded: boolean;
  onToggleMonth: () => void;
}) {
  const [typesOpen, setTypesOpen] = React.useState(false);
  const typesId = React.useId();

  return (
    <div>
      <PeriodNav
        layout="compact"
        title={cal.title}
        unit={cal.view === 'year' ? 'season' : 'month'}
        onPrev={cal.goPrev}
        onNext={cal.goNext}
        prevDisabled={cal.prevDisabled}
        nextDisabled={cal.nextDisabled}
        titleClassName="px-2 text-[16px] font-medium text-[var(--t-text)]"
      >
        {cal.view === 'month' ? (
          <button
            type="button"
            aria-expanded={monthExpanded}
            onClick={onToggleMonth}
            className="cc-focus flex cursor-pointer items-center gap-[6px] rounded-[8px] border-0 bg-transparent px-2 py-1 text-[16px] font-medium text-[var(--t-text)]"
          >
            <span aria-live="polite">{cal.title}</span>
            {monthExpanded ? (
              <IconCaretUp size={13} className="text-[var(--t-text-2)]" />
            ) : (
              <IconCaretDown size={13} className="text-[var(--t-text-2)]" />
            )}
          </button>
        ) : undefined}
      </PeriodNav>

      <div className="mt-2 grid grid-cols-[minmax(0,1fr)_auto] gap-2">
        <ViewSegment options={MOBILE_VIEWS} view={cal.view} onView={cal.setView} disabledViews={cal.disabledViews} />
        <button
          type="button"
          aria-expanded={typesOpen}
          aria-controls={typesId}
          onClick={() => setTypesOpen((o) => !o)}
          className={`cc-focus flex h-[38px] cursor-pointer items-center gap-[6px] rounded-[10px] border bg-[var(--t-surface)] px-3 text-[13px] font-semibold text-[var(--t-text)] ${
            cal.typeFilter.length > 0 ? 'border-[var(--t-accent)]' : 'border-[var(--t-border)]'
          }`}
        >
          <IconFunnel size={15} />
          Type
          {cal.typeFilter.length > 0 && <span className="text-[var(--t-accent-text)]">{cal.typeFilter.length}</span>}
        </button>
      </div>

      <div id={typesId} aria-hidden={!typesOpen} className={`cc-types ${typesOpen ? 'cc-types--open' : ''}`}>
        <div>
          <TypeFilter
            variant="tiles"
            focusable={typesOpen}
            typeFilter={cal.typeFilter}
            onTypeFilter={cal.setTypeFilter}
            counts={cal.counts}
            className="p-1 pt-2"
          />
        </div>
      </div>
    </div>
  );
}

/** Month на телефоне: неделя/месяц и под ними кубы выбранного дня. */
function MonthBody({ cal, expanded }: { cal: CalendarController; expanded: boolean }) {
  const dayMeets = cal.selectedDay ? meetsOnDay(cal.monthMeets, cal.selectedDay) : [];
  return (
    <div>
      <MonthMobile
        month={cal.month}
        meets={cal.monthMeets}
        today={cal.today}
        selectedDay={cal.selectedDay}
        onSelectDay={cal.selectDay}
        expanded={expanded}
        onPrevMonth={() => cal.goMonth(addMonths(cal.month, -1))}
        onNextMonth={() => cal.goMonth(addMonths(cal.month, 1))}
        prevDisabled={cal.prevDisabled}
        nextDisabled={cal.nextDisabled}
      />
      <div aria-live="polite" className="mt-4">
        {cal.monthMeets.length === 0 && <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets this month</p>}
        {cal.monthMeets.length > 0 && cal.selectedDay && dayMeets.length === 0 && (
          <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets on this day.</p>
        )}
        {dayMeets.length > 0 && <CubeGrid meets={dayMeets} today={cal.today} />}
      </div>
    </div>
  );
}

function CalendarMobile({ cal }: { cal: CalendarController }) {
  // Month: месяц развёрнут целиком (иначе одна неделя). В адрес не пишется.
  const [monthExpanded, setMonthExpanded] = React.useState(false);

  let body: React.ReactNode;
  if (cal.error) body = <CalendarError onRetry={cal.retry} />;
  else if (!cal.data) body = <CalendarLoading />;
  else if (cal.view === 'year') {
    body = <YearView season={cal.season} meets={cal.seasonMeets} today={cal.today} onPickMonth={cal.openMonth} />;
  } else if (cal.view === 'month') {
    body = <MonthBody cal={cal} expanded={monthExpanded} />;
  } else {
    body = <MeetDaysView month={cal.month} meets={cal.monthMeets} today={cal.today} />;
  }

  return (
    <div>
      <MobileControls cal={cal} monthExpanded={monthExpanded} onToggleMonth={() => setMonthExpanded((v) => !v)} />
      <div className="mt-6">{body}</div>
      {cal.data && !cal.error && <CalendarLegend />}
    </div>
  );
}

export default CalendarMobile;
