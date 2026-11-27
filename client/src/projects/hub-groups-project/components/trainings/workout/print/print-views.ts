// Реестр видов печати (docs/plans/workout-print-plan.md §4). Новый вид = файл `views/<id>-view.tsx`
// + строка здесь; убрать вид — убрать строку. Окно предпросмотра само покажет сегмент выбора, когда
// видов больше одного, и спрячет вид, у которого `available(source)` — false. Порядок строк = порядок
// сегментов: Large · Numbers · Two sides (хендофф 27.11, вторая версия; Rows и Two sides в виде
// строк отклонены — запомненный `rows` / `two-sides` открывает вид по умолчанию, `pickView`).

import type { WorkoutPrintView } from './print-types';
import { LARGE_VIEW } from './views/large-view';
import { NUMBERS_TWO_SIDES_VIEW } from './views/numbers-two-sides-view';
import { NUMBERS_VIEW } from './views/numbers-view';

export const PRINT_VIEWS = {
  large: LARGE_VIEW,
  numbers: NUMBERS_VIEW,
  'numbers-two-sides': NUMBERS_TWO_SIDES_VIEW,
} satisfies Record<string, WorkoutPrintView>;

export type WorkoutPrintViewId = keyof typeof PRINT_VIEWS;

/** Вид, который окно открывает первым (если он годится для workout; иначе — первый годный). */
export const DEFAULT_PRINT_VIEW: WorkoutPrintViewId = 'large';
