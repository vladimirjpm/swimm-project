// Какие виды печати показать в окне и какой выбран (без React и DOM — гоняет `print-view-pick.test.mjs`).

import type { WorkoutPrintSource, WorkoutPrintView } from './print-types';

/** Годные для этого workout виды в порядке реестра. */
export function availableViews(views: Record<string, WorkoutPrintView>, source: WorkoutPrintSource): WorkoutPrintView[] {
  return Object.values(views).filter((v) => v.available?.(source) ?? true);
}

/** Выбранный вид: `wanted`, если он среди годных, иначе первый годный; годных нет — null. */
export function pickView(list: WorkoutPrintView[], wanted: string | null | undefined): WorkoutPrintView | null {
  return list.find((v) => v.id === wanted) ?? list[0] ?? null;
}
