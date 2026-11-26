// Реестр видов печати (docs/plans/workout-print-plan.md §4). Новый вид = файл `views/<id>-view.tsx`
// + строка в `PRINT_VIEWS`; окно и кнопки не меняются.
//
// ЗАГЛУШКА: пока вид `large` (Ш3, `views/large-view.tsx`) не влит, под `large` стоит лист-заглушка Ш2.
// Замена после мержа Ш3 — строка реестра `large: LARGE_VIEW` + удалить `STUB_LARGE_VIEW` и два
// импорта заглушки (`createElement`, `PrintSheetStub`) вместе с `print-sheet-stub.tsx`.

import { createElement, type FC } from 'react';
import { PrintSheetStub } from './print-sheet-stub';
import type { WorkoutPrintSource, WorkoutPrintView, WorkoutPrintViewId } from './print-types';

const StubSheet: FC<{ source: WorkoutPrintSource }> = () => createElement(PrintSheetStub);

const STUB_LARGE_VIEW: WorkoutPrintView = {
  id: 'large',
  label: 'Large',
  orientation: 'portrait',
  Sheet: StubSheet,
};

export const PRINT_VIEWS: Record<WorkoutPrintViewId, WorkoutPrintView> = { large: STUB_LARGE_VIEW };
export const DEFAULT_PRINT_VIEW: WorkoutPrintViewId = 'large';
