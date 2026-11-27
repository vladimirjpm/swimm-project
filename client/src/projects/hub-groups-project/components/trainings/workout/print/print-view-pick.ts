// Какие виды печати показать в окне и какой выбран (без React и DOM — гоняет `print-view-pick.test.mjs`).

import type { WorkoutPrintSource, WorkoutPrintView } from './print-types';

/** Годные для этого workout виды в порядке реестра. */
export function availableViews(views: Record<string, WorkoutPrintView>, source: WorkoutPrintSource): WorkoutPrintView[] {
  return Object.values(views).filter((v) => v.available?.(source) ?? true);
}

/**
 * Выбранный вид: `wanted`, если он среди годных; иначе `fallback` (вид по умолчанию), если годен;
 * иначе первый годный; годных нет — null.
 */
export function pickView(
  list: WorkoutPrintView[],
  wanted: string | null | undefined,
  fallback?: string | null,
): WorkoutPrintView | null {
  return list.find((v) => v.id === wanted) ?? list.find((v) => v.id === fallback) ?? list[0] ?? null;
}

// ── Запомненный вид (В21, Влад 27.11.2026) ─────────────────────────────────────

/** Ключ `localStorage`: последний вид, который выбрали в окне печати. */
export const PRINT_VIEW_STORAGE_KEY = 'swimm.workoutPrint.view';

type ViewStorage = Pick<Storage, 'getItem' | 'setItem'>;

/**
 * Запомненный вид или null (не выбирали, хранилища нет, Safari в приватном режиме бросает).
 * Несуществующий или недоступный для workout вид отсеет `pickView` — откроется вид по умолчанию.
 */
export function readSavedView(storage: ViewStorage | null | undefined): string | null {
  try {
    return storage?.getItem(PRINT_VIEW_STORAGE_KEY) || null;
  } catch {
    return null;
  }
}

/** Запомнить выбранный вид; хранилище недоступно — молча ничего. */
export function saveView(storage: ViewStorage | null | undefined, id: string): void {
  try {
    storage?.setItem(PRINT_VIEW_STORAGE_KEY, id);
  } catch {
    // квота / приватный режим — выбор просто не запомнится
  }
}
