import type { Workout, WorkoutLine, WorkoutSet } from '../../../types';

/**
 * Метры workout по уровням (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6): считает клиент,
 * сервер их не хранит. Метры уровня = сумма по сетам, которые он плывёт:
 * `repeat` сета × повторы (своё число уровня, иначе строки) × дистанция. Строка без дистанции —
 * 0 м. Тесты — `workout-totals.test.mjs`.
 *
 * `levelId` null — дорожка без уровня: только сеты «для всех», без своих повторов.
 */

/** Плывёт ли уровень этот сет (галочка над колонкой). Та же проверка — в `workout-for-level.ts`. */
const swims = (set: WorkoutSet, levelId: number | null) =>
  set.level_ids == null || (levelId != null && set.level_ids.includes(levelId));

export function lineMeters(line: WorkoutLine, levelId: number | null): number {
  const own = levelId != null ? line.per_level?.[String(levelId)]?.reps : null;
  return (own ?? line.reps) * (line.distance ?? 0);
}

/** Метры сета для уровня; сет не его — 0. */
export function setMeters(set: WorkoutSet, levelId: number | null): number {
  if (!swims(set, levelId)) return 0;
  return set.repeat * set.lines.reduce((sum, line) => sum + lineMeters(line, levelId), 0);
}

export function levelMeters(workout: Workout, levelId: number | null): number {
  return workout.sets.reduce((sum, set) => sum + setMeters(set, levelId), 0);
}

/** «2.8 km»: километры с одним знаком (так пишут на листе: 2.1, 2.8). */
export function formatKm(meters: number): string {
  return `${(Math.round(meters / 100) / 10).toFixed(1)} km`;
}

/** «750 m». */
export function formatMeters(meters: number): string {
  return `${meters} m`;
}

/** Разброс по уровням: «2.1–2.8 km», все равны — «2.8 km»; пусто — null. */
export function kmRange(meters: number[]): string | null {
  if (meters.length === 0) return null;
  const lo = Math.min(...meters);
  const hi = Math.max(...meters);
  if (formatKm(lo) === formatKm(hi)) return formatKm(hi);
  return `${formatKm(lo).replace(' km', '')}–${formatKm(hi)}`;
}

/** Разброс метров одного сета: «650–750 m» / «750 m». */
export function metersRange(meters: number[]): string {
  const lo = Math.min(...meters);
  const hi = Math.max(...meters);
  return lo === hi ? formatMeters(hi) : `${lo}–${hi} m`;
}
