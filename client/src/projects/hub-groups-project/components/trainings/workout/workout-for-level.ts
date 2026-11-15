import type { Workout, WorkoutSet } from '../../../types';

/**
 * «Workout для уровня» (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6) — что получит дорожка
 * уровня: сеты, отмеченные для него (или для всех), в ячейках — только его время. Зеркало
 * серверного `WorkoutRules.ForLevel` (участник получает версию от сервера; тренеру превью считает
 * клиент). Тесты — `workout-for-level.test.mjs`.
 */

/** Плывёт ли уровень этот сет: галочек нет — плывут все. Та же проверка — в `workout-totals.ts`. */
export function setSwumBy(set: WorkoutSet, levelId: number | null): boolean {
  return set.level_ids == null || (levelId != null && set.level_ids.includes(levelId));
}

/** `levelId` null — дорожка без уровня: только сеты для всех и без времён. Ничего нет — null. */
export function workoutForLevel(workout: Workout | null | undefined, levelId: number | null): Workout | null {
  if (!workout) return null;
  const key = levelId == null ? null : String(levelId);
  const sets = workout.sets
    .filter((set) => setSwumBy(set, levelId))
    .map((set) => ({
      ...set,
      level_ids: null,
      lines: set.lines.map((line) => {
        const cell = key != null ? line.per_level?.[key] : undefined;
        return { ...line, per_level: cell && key != null ? { [key]: { ...cell } } : null };
      }),
    }));
  if (sets.length === 0 && !workout.note) return null;
  return { note: workout.note ?? null, sets };
}

/** Есть ли что показывать: хоть один сет. */
export function hasWorkout(workout: Workout | null | undefined): workout is Workout {
  return !!workout && workout.sets.length > 0;
}
