import type { TrainingLane } from '../../../types';
import { hasWorkout } from '../workout/workout-for-level';

/**
 * Workout прошедшего занятия по уровням (решение Влада 26.11.2026): сервер кладёт версию уровня на
 * КАЖДУЮ дорожку, а дорожки одного уровня несут одну и ту же версию — карточка одна на уровень.
 * Своя (дорожка зрителя) — первой; остальные — в порядке дорожек. Чистая функция — тесты в
 * `past-workout-levels.test.mjs`.
 */
export interface PastLevelWorkout {
  /** Ключ уровня: id или `none` (дорожка без уровня). */
  key: string;
  /** Дорожка-представитель: её `level` и `level_workout`. */
  lane: TrainingLane;
  /** Все дорожки этого уровня — подпись «Lanes 1, 2». */
  laneNos: number[];
  /** Уровень своей дорожки зрителя. */
  mine: boolean;
}

export function pastLevelWorkouts(lanes: readonly TrainingLane[], myLaneNo: number | null): PastLevelWorkout[] {
  const byKey = new Map<string, PastLevelWorkout>();
  for (const lane of [...lanes].sort((a, b) => a.lane_no - b.lane_no)) {
    if (!hasWorkout(lane.level_workout)) continue;
    const key = lane.level ? String(lane.level.id) : 'none';
    const entry = byKey.get(key);
    if (entry) {
      entry.laneNos.push(lane.lane_no);
      if (lane.lane_no === myLaneNo) entry.mine = true;
    } else {
      byKey.set(key, { key, lane, laneNos: [lane.lane_no], mine: lane.lane_no === myLaneNo });
    }
  }
  const list = [...byKey.values()];
  return [...list.filter((l) => l.mine), ...list.filter((l) => !l.mine)];
}

/** «Lane 2» / «Lanes 1, 2». */
export function lanesLabel(laneNos: readonly number[]): string {
  return `${laneNos.length === 1 ? 'Lane' : 'Lanes'} ${laneNos.join(', ')}`;
}
