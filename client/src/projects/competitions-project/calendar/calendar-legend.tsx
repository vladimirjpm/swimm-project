import React from 'react';
import { DayStatusMark } from './status-mark';

/*
 * Легенда календаря: образцы совпадают с тем, что рисуют ячейки дня (`DayGrid`): RES — протокол,
 * SL — стартлист, точка — старт без того и другого (хендофф competitions_home-4, 10c), золотая
 * полоса чемпионата, рамка «сегодня».
 * `className` — отступы и кегль от места (сайдбар десктопа — 11px, под видом телефона — 12px).
 */
function CalendarLegend({ className = 'mt-2 text-[12px]' }: { className?: string }) {
  return (
    <div className={`flex flex-wrap items-center gap-x-3 gap-y-[6px] text-[var(--t-text-2)] ${className}`}>
      <span className="flex items-center gap-[5px]">
        <DayStatusMark status="results" />
        Results
      </span>
      <span className="flex items-center gap-[5px]">
        <DayStatusMark status="startlist" />
        Startlist
      </span>
      <span className="flex items-center gap-[5px]">
        <DayStatusMark status="upcoming" />
        Meet
      </span>
      <span className="flex items-center gap-[5px]">
        <span
          aria-hidden="true"
          className="h-[10px] w-[10px] rounded-[3px] border border-[var(--t-champ-strip-border)] bg-[var(--t-champ-strip-bg)]"
        />
        Championship
      </span>
      <span className="flex items-center gap-[5px]">
        <span aria-hidden="true" className="h-[10px] w-[10px] rounded-[3px] border border-[var(--t-accent-border)]" />
        Today
      </span>
    </div>
  );
}

export default CalendarLegend;
