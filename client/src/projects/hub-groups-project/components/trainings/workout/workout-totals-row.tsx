import React from 'react';
import type { Workout } from '../../../types';
import { LevelTag, levelCellStyle, type WorkoutLevel } from './workout-level-tag';
import { formatKm, levelMeters } from './workout-totals';

/**
 * Итог workout по уровням (CF6, макет F6 внизу): «① Advanced 2.8 km» плашкой бледного тона цвета
 * уровня. Считается на лету (`levelMeters`), сервер метры не хранит. Уровней нет — одна сумма.
 */
function WorkoutTotalsRow({ workout, levels, full }: { workout: Workout; levels: WorkoutLevel[]; full: boolean }) {
  return (
    <div className="flex min-w-0 flex-wrap items-center gap-1.5">
      <span className="mr-1 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>Total</span>
      {levels.length === 0 ? (
        <span className="hp-mono text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{formatKm(levelMeters(workout, null))}</span>
      ) : levels.map((level) => (
        <span
          key={level.id}
          className="inline-flex min-w-0 items-center gap-1.5 rounded-full px-2.5 py-1"
          style={levelCellStyle(level)}
        >
          <LevelTag level={level} full={full} size={16} />
          <span className="hp-mono flex-none text-[12px] font-extrabold">{formatKm(levelMeters(workout, level.id))}</span>
        </span>
      ))}
    </div>
  );
}

export default WorkoutTotalsRow;
