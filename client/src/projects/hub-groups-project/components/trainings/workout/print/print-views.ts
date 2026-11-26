// Реестр видов печати (docs/plans/workout-print-plan.md §4). Новый вид = файл `views/<id>-view.tsx`
// + строка здесь; убрать вид — убрать строку. Окно предпросмотра само покажет сегмент выбора, когда
// видов больше одного, и спрячет вид, у которого `available(source)` — false. Порядок строк = порядок
// сегментов. Пример вида в два листа (duplex) — стенд `src/dev/ui-kit/print-demo-views.tsx`.

import type { WorkoutPrintView } from './print-types';
import { LARGE_VIEW } from './views/large-view';

export const PRINT_VIEWS = {
  large: LARGE_VIEW,
} satisfies Record<string, WorkoutPrintView>;

export type WorkoutPrintViewId = keyof typeof PRINT_VIEWS;

/** Вид, который окно открывает первым (если он годится для workout; иначе — первый годный). */
export const DEFAULT_PRINT_VIEW: WorkoutPrintViewId = 'large';
