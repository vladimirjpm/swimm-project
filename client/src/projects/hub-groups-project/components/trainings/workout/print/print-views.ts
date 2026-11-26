// Реестр видов печати (docs/plans/workout-print-plan.md §4). Новый вид = файл `views/<id>-view.tsx`
// + строка в `PRINT_VIEWS`; окно и кнопки не меняются.

import type { WorkoutPrintView, WorkoutPrintViewId } from './print-types';
import { LARGE_VIEW } from './views/large-view';

export const PRINT_VIEWS: Record<WorkoutPrintViewId, WorkoutPrintView> = { large: LARGE_VIEW };
export const DEFAULT_PRINT_VIEW: WorkoutPrintViewId = 'large';
