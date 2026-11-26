/**
 * Стенд вида печати `large` (Ш3, docs/plans/workout-print-plan.md §3, §8.2): лист без группы и
 * занятия — на фикстурах `print-fixtures.ts`. Сцена показывает лист в масштабе 0.6 и подпись
 * подгонки; `Open print preview` — упрощённое окно печати (как стенд Ш2), чтобы снять PDF.
 * Настоящее окно с кнопкой Print — Ш4.
 */
import React, { useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { workoutForLevel } from '../../projects/hub-groups-project/components/trainings/workout/workout-for-level';
import { usePrintSession } from '../../projects/hub-groups-project/components/trainings/workout/print/use-print-session';
import type { PrintSheetFit, WorkoutPrintSource } from '../../projects/hub-groups-project/components/trainings/workout/print/print-types';
import { LargeSheet } from '../../projects/hub-groups-project/components/trainings/workout/print/views/large-view';
import type { Workout } from '../../projects/hub-groups-project/types';
import {
  ENGLISH, FIXTURE_GROUP, FIXTURE_LEVEL, FIXTURE_LEVEL_ID, HUGE_224, LONG_40, SHEET_2211, SHEET_2411, SHEET_2611, withMain,
} from './print-fixtures';

export const PRINT_FIXTURES: Record<string, { label: string; workout: Workout }> = {
  'mid-main': { label: '26.11 · main ④', workout: withMain(SHEET_2611, [4]) },
  mid: { label: '26.11 · no main', workout: SHEET_2611 },
  short: { label: '22.11 · short', workout: SHEET_2211 },
  'long-main': { label: '24.11 · main ④', workout: withMain(SHEET_2411, [4]) },
  long: { label: '24.11 · no main', workout: SHEET_2411 },
  'long-40': { label: '40 lines · main ⑤', workout: LONG_40 },
  huge: { label: '224 lines · overflow', workout: HUGE_224 },
  english: { label: 'English · ltr', workout: ENGLISH },
};

const btn = 'rounded-md border border-white/40 px-3 py-1.5 text-sm font-semibold text-white hover:bg-white/10';

/** Окно печати — минимальный макет окна Ш4 на контракте `workout-print.css`. */
const PrintWindow: React.FC<{ source: WorkoutPrintSource; onClose: () => void }> = ({ source, onClose }) => {
  const { root, print } = usePrintSession('portrait');
  if (!root) return null;
  return createPortal(
    <div className="fixed inset-0 z-[1000] flex flex-col items-center overflow-auto bg-[rgba(2,10,30,0.88)] p-4">
      <div className="wprint-noprint mb-3 flex items-center gap-3 text-white">
        <span className="font-bold">Print preview</span>
        <button type="button" className={btn} onClick={print} data-testid="wprint-print">Print</button>
        <button type="button" className={btn} onClick={onClose} data-testid="wprint-close">Close</button>
      </div>
      <div style={{ width: '126mm', height: '178.2mm' }}>
        <div className="wprint-stage origin-top-left shadow-2xl" style={{ transform: 'scale(0.6)' }}>
          <LargeSheet source={source} />
        </div>
      </div>
    </div>,
    root,
  );
};

const PrintLargePreview: React.FC<{ fixture: string }> = ({ fixture }) => {
  const [open, setOpen] = useState(false);
  const [fit, setFit] = useState<PrintSheetFit | null>(null);
  const fx = PRINT_FIXTURES[fixture] ?? PRINT_FIXTURES['mid-main'];
  const source = useMemo<WorkoutPrintSource>(
    () => ({
      workout: workoutForLevel(fx.workout, FIXTURE_LEVEL_ID) ?? { sets: [] },
      levelId: FIXTURE_LEVEL_ID,
      level: FIXTURE_LEVEL,
      groupName: fx === PRINT_FIXTURES.english ? 'Dolphin Masters' : FIXTURE_GROUP,
    }),
    [fx],
  );
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          className="rounded-md bg-sky-600 px-3 py-1.5 text-sm font-semibold text-white"
          onClick={() => setOpen(true)}
          data-testid="wprint-open"
        >
          Open print preview
        </button>
        <span className="text-sm font-semibold" data-testid="wprint-caption">
          {fit ? fit.caption : '…'}
          {fit?.overflow && ' · Too long for one page'}
        </span>
      </div>
      <div style={{ width: '126mm', height: '178.2mm' }} className="overflow-hidden">
        <div className="origin-top-left shadow-xl" style={{ transform: 'scale(0.6)' }}>
          <LargeSheet source={source} onFit={setFit} />
        </div>
      </div>
      {open && <PrintWindow source={source} onClose={() => setOpen(false)} />}
    </div>
  );
};

export default PrintLargePreview;
