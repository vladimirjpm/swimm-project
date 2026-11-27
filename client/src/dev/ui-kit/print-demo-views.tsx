/**
 * Стенд окна печати workout с реестром ПРИМЕРОВ видов (docs/plans/workout-print-plan.md §4): как
 * выглядит вид в два листа. В прод-реестр `PRINT_VIEWS` пример не входит — это образец, с которого
 * списывать настоящий вид: файл `views/<id>-view.tsx` + строка в `print-views.ts`.
 *
 * `large-notes` — лицо: обычный лист `large`; оборот: лист заметок тренера. `duplex: true` — окно
 * подсказывает включить двустороннюю печать (включить её сама страница не может).
 */
import React, { useMemo, useState } from 'react';
import { workoutForLevel } from '../../projects/hub-groups-project/components/trainings/workout/workout-for-level';
import type { WorkoutPrintSource, WorkoutPrintView } from '../../projects/hub-groups-project/components/trainings/workout/print/print-types';
import { LARGE_VIEW, LargeSheet } from '../../projects/hub-groups-project/components/trainings/workout/print/views/large-view';
import { WorkoutPrintDialog } from '../../projects/hub-groups-project/components/trainings/workout/print/workout-print-dialog';
import { FIXTURE_GROUP, FIXTURE_LEVEL, FIXTURE_LEVEL_ID, SHEET_2611, withMain } from './print-fixtures';

/** Оборот: шапка и линейки для заметок. Обычный `.wprint-sheet` — разрыв страницы перед ним ставит CSS. */
const NotesSheet: React.FC<{ source: WorkoutPrintSource }> = ({ source }) => (
  <div className="wprint-sheet" data-orientation="portrait" style={{ padding: '14mm', fontFamily: 'sans-serif' }}>
    <div style={{ fontSize: 22, fontWeight: 800, borderBottom: '2px solid #000', paddingBottom: 8 }}>
      {source.groupName} · Notes
    </div>
    {Array.from({ length: 22 }, (_, i) => (
      <div key={i} style={{ borderBottom: '1px solid #999', height: '11mm' }} />
    ))}
  </div>
);

const LargeWithNotes: WorkoutPrintView['Sheet'] = ({ source, onFit }) => (
  <>
    <LargeSheet source={source} onFit={onFit} />
    <NotesSheet source={source} />
  </>
);

export const DEMO_PRINT_VIEWS: Record<string, WorkoutPrintView> = {
  large: LARGE_VIEW,
  'large-notes': {
    id: 'large-notes',
    label: 'Large + notes',
    orientation: 'portrait',
    duplex: true,
    Sheet: LargeWithNotes,
  },
};

const PrintDemoViews: React.FC<{ view: string }> = ({ view }) => {
  const [open, setOpen] = useState(false);
  const source = useMemo<WorkoutPrintSource>(
    () => ({
      workout: workoutForLevel(withMain(SHEET_2611, [4]), FIXTURE_LEVEL_ID) ?? { sets: [] },
      levelId: FIXTURE_LEVEL_ID,
      level: FIXTURE_LEVEL,
      groupName: FIXTURE_GROUP,
    }),
    [],
  );
  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        className="self-start rounded-md bg-sky-600 px-3 py-1.5 text-sm font-semibold text-white"
        onClick={() => setOpen(true)}
        data-testid="wprint-demo-open"
      >
        Open print dialog
      </button>
      {open && (
        <WorkoutPrintDialog source={source} views={DEMO_PRINT_VIEWS} defaultView={view} remember={false} onClose={() => setOpen(false)} />
      )}
    </div>
  );
};

export default PrintDemoViews;
