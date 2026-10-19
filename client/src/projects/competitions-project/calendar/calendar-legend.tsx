import React from 'react';

/*
 * Легенда под телом вида: образцы совпадают с тем, что рисуют ячейки и кубы
 * (в макете там точки — заменены образцами, чтобы легенда не расходилась с вёрсткой).
 */
function CalendarLegend() {
  return (
    <div className="mt-2 flex flex-wrap items-center gap-[14px] text-[12px] text-[var(--t-text-2)]">
      <span className="flex items-center gap-[6px]">
        <span
          aria-hidden="true"
          className="h-[10px] w-[10px] rounded-[3px] border border-[var(--t-champ-border)] bg-[var(--t-champ-chip-bg)]"
        />
        Championship
      </span>
      <span className="flex items-center gap-[6px]">
        <span
          aria-hidden="true"
          className="h-[10px] w-[10px] rounded-[3px] border border-[var(--t-border)] bg-[var(--t-nested)]"
        />
        Meet
      </span>
      <span className="flex items-center gap-[6px]">
        <span aria-hidden="true" className="h-[10px] w-[10px] rounded-[2px] border-[1.5px] border-[var(--t-accent)]" />
        Today
      </span>
    </div>
  );
}

export default CalendarLegend;
