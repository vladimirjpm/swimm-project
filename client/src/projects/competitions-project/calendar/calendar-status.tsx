import React from 'react';

/*
 * Заглушки тела календаря: загрузка и ошибка с «Try again». Общие для обеих раскладок.
 */

const MUTED = 'm-0 text-[14px] text-[var(--t-text-2)]';

export function CalendarLoading() {
  return (
    <div className="flex min-h-[320px] items-start">
      <p className={MUTED}>Loading calendar…</p>
    </div>
  );
}

export function CalendarError({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="flex min-h-[120px] flex-col items-start gap-3">
      <p className={MUTED}>Could not load the calendar.</p>
      <button
        type="button"
        onClick={onRetry}
        className="cc-focus cursor-pointer rounded-[10px] border border-[var(--t-border)] bg-[var(--t-surface)] px-4 py-2 text-[13px] font-semibold text-[var(--t-text)] hover:border-[var(--t-accent)]"
      >
        Try again
      </button>
    </div>
  );
}
