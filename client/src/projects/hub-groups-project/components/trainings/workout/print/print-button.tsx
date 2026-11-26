import React, { useState } from 'react';
import { DEEP_DESKTOP_QUERY, useMediaQuery } from '../../../../../../hooks/useMediaQuery';
import type { WorkoutPrintSource } from './print-types';
import { WorkoutPrintDialog } from './workout-print-dialog';

/**
 * Кнопка `Print` у workout (docs/plans/workout-print-plan.md §5): открывает окно предпросмотра листа.
 * Ставится только там, где workout уже показан (права те же, новых нет). Десктоп — подпись,
 * телефон — значок 36×36 с `aria-label="Print workout"`.
 */
export function PrintButton({ source }: { source: WorkoutPrintSource }) {
  const desktop = useMediaQuery(DEEP_DESKTOP_QUERY);
  const [open, setOpen] = useState(false);
  return (
    <>
      <button
        type="button"
        aria-label="Print workout"
        onClick={() => setOpen(true)}
        className={`inline-flex flex-none cursor-pointer items-center justify-center gap-1.5 rounded-[10px] border font-extrabold ${desktop ? 'h-8 px-3 text-[12px]' : 'h-9 w-9 p-0'}`}
        style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg-row)', color: 'var(--deep-text)' }}
      >
        <PrinterIcon />
        {desktop && 'Print'}
      </button>
      {open && <WorkoutPrintDialog source={source} onClose={() => setOpen(false)} />}
    </>
  );
}

function PrinterIcon() {
  return (
    <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 9V3h12v6" />
      <rect x="3" y="9" width="18" height="9" rx="2" />
      <path d="M6 14h12v7H6z" />
    </svg>
  );
}
