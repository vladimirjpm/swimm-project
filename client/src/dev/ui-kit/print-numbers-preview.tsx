/**
 * Стенд видов печати `numbers` и `numbers-two-sides` (хендофф дизайнера 27.11.2026, вторая версия;
 * docs/plans/workout-print-plan.md §4.3, §8.2): листы на фикстурах `print-fixtures.ts` в масштабе 0.5 и подпись подгонки; `Open print
 * dialog` — НАСТОЯЩЕЕ окно печати с прод-реестром `PRINT_VIEWS` (сегмент Large · Numbers · Two sides),
 * открытое на выбранном виде, — с него снимается PDF.
 */
import React, { useMemo, useState } from 'react';
import { workoutForLevel } from '../../projects/hub-groups-project/components/trainings/workout/workout-for-level';
import type { PrintSheetFit, WorkoutPrintSource } from '../../projects/hub-groups-project/components/trainings/workout/print/print-types';
import { NumbersSheet } from '../../projects/hub-groups-project/components/trainings/workout/print/views/numbers-view';
import { NumbersTwoSidesSheet } from '../../projects/hub-groups-project/components/trainings/workout/print/views/numbers-two-sides-view';
import { WorkoutPrintDialog } from '../../projects/hub-groups-project/components/trainings/workout/print/workout-print-dialog';
import type { Workout } from '../../projects/hub-groups-project/types';
import {
  ENGLISH, FIXTURE_GROUP, FIXTURE_LEVEL, FIXTURE_LEVEL_ID, HUGE_224, SHEET_2211, SHEET_2411, SHEET_2611, withMain,
} from './print-fixtures';

type Level = { rank: number; name: string };

export const NUMBERS_FIXTURES: Record<string, { label: string; workout: Workout; level?: Level; group?: string }> = {
  'mid-main': { label: '26.11 · Intermediate · main ④', workout: withMain(SHEET_2611, [4]) },
  // Ячейки Advanced (id 37) и Intermediate в листе 24.11 одинаковые — подпись уровня как в макете.
  'long-main': { label: '24.11 · Advanced · main ④', workout: withMain(SHEET_2411, [4]), level: { rank: 1, name: 'Advanced' } },
  short: { label: '22.11 · no main', workout: SHEET_2211 },
  english: { label: 'English · ltr · main ④', workout: withMain(ENGLISH, [4]), group: 'Dolphin Masters' },
  huge: { label: '224 lines · overflow', workout: HUGE_224 },
};

export const NUMBERS_STAND_VIEWS = ['numbers', 'numbers-two-sides'] as const;

const PrintNumbersPreview: React.FC<{ fixture: string; view: string }> = ({ fixture, view }) => {
  const [open, setOpen] = useState(false);
  const [fit, setFit] = useState<PrintSheetFit | null>(null);
  const fx = NUMBERS_FIXTURES[fixture] ?? NUMBERS_FIXTURES['mid-main'];
  const source = useMemo<WorkoutPrintSource>(
    () => ({
      workout: workoutForLevel(fx.workout, FIXTURE_LEVEL_ID) ?? { sets: [] },
      levelId: FIXTURE_LEVEL_ID,
      level: fx.level ?? FIXTURE_LEVEL,
      groupName: fx.group ?? FIXTURE_GROUP,
    }),
    [fx],
  );
  const Sheet = view === 'numbers-two-sides' ? NumbersTwoSidesSheet : NumbersSheet;
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          className="rounded-md bg-sky-600 px-3 py-1.5 text-sm font-semibold text-white"
          onClick={() => setOpen(true)}
          data-testid="wprint-open"
        >
          Open print dialog
        </button>
        <span className="text-sm font-semibold" data-testid="wprint-caption">
          {fit ? fit.caption : '…'}
          {fit?.overflow && ' · Too long for one page'}
        </span>
      </div>
      <div className="flex flex-wrap gap-4">
        {/* Каждый лист — 210×297 мм в масштабе 0.5; листы вида идут подряд, как в окне печати. */}
        <div className="flex flex-wrap gap-4 [&>.wprint-sheet]:[zoom:0.5]">
          <Sheet key={`${fixture}-${view}`} source={source} onFit={setFit} />
        </div>
      </div>
      {view === 'numbers-two-sides' && !fx.workout.sets.some((s) => (s as { main?: boolean }).main) && (
        <p className="text-sm">Two sides is not available for this workout (no main set) — the dialog hides it.</p>
      )}
      {open && <WorkoutPrintDialog source={source} defaultView={view} remember={false} onClose={() => setOpen(false)} />}
    </div>
  );
};

export default PrintNumbersPreview;
