import React from 'react';
import CalendarLegend from './calendar-legend';
import { useTopbarHeight } from './calendar-layout-hooks';
import { monthGrid } from './calendar-model';
import type { CalendarView } from './calendar-state';
import DayGrid from './day-grid';
import PeriodNav from './period-nav';
import TypeFilter from './type-filter';
import type { CalendarController } from './use-calendar-controller';
import ViewSegment, { type ViewOption } from './view-segment';

/*
 * Левая колонка лейаута 9a — липкая карточка (хендофф competitions_home-3, 2b):
 * сегмент Year · Month → ‹ October 2026 › → мини-месяц (только в Month) → легенда → TYPE.
 * Клик по дню мини-месяца — режим дня справа, повторный клик по тому же дню — назад к месяцу.
 * Все части — отдельные блоки (`ViewSegment`, `PeriodNav`, `DayGrid`, `CalendarLegend`,
 * `TypeFilter`): карточку можно пересобрать, не трогая их.
 */

const DESKTOP_VIEWS: ViewOption[] = [
  { value: 'year', label: 'Year' },
  { value: 'month', label: 'Month' },
];

function CalendarSidebar({ cal }: { cal: CalendarController }) {
  const topbar = useTopbarHeight();
  const weeks = React.useMemo(() => monthGrid(cal.month), [cal.month]);
  const views: CalendarView[] = cal.disabledViews.filter((v) => v !== 'meets');

  return (
    <aside
      aria-label="Calendar navigation"
      className="flex flex-col gap-[10px] rounded-[16px] border border-[var(--t-border-2)] bg-[var(--t-surface)] p-3 shadow-[var(--t-shadow)] backdrop-blur-[14px] sticky"
      style={{ top: topbar + 12 }}
    >
      <ViewSegment options={DESKTOP_VIEWS} view={cal.view} onView={cal.setView} disabledViews={views} />

      <PeriodNav
        title={cal.title}
        unit={cal.view === 'year' ? 'season' : 'month'}
        onPrev={cal.goPrev}
        onNext={cal.goNext}
        prevDisabled={cal.prevDisabled}
        nextDisabled={cal.nextDisabled}
      />

      {cal.view === 'month' && cal.data && (
        <DayGrid
          size="sm"
          weeks={weeks}
          meets={cal.monthMeets}
          today={cal.today}
          selectedDay={cal.selectedDay}
          onSelectDay={(iso) => cal.selectDay(iso === cal.selectedDay ? null : iso)}
        />
      )}

      <CalendarLegend className="pt-[2px] text-[11px]" />

      <div className="flex flex-col gap-[6px] border-t border-[var(--t-border-2)] pt-[10px]">
        <span className="text-[11px] uppercase tracking-[.1em] text-[var(--t-text-2)]">Type</span>
        <TypeFilter variant="chips" typeFilter={cal.typeFilter} onTypeFilter={cal.setTypeFilter} counts={cal.counts} />
      </div>
    </aside>
  );
}

export default CalendarSidebar;
