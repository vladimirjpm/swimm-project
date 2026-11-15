import React from 'react';
import type { Workout } from '../../../types';
import WorkoutLineLabel from './workout-line-label';

/**
 * Workout ОДНОГО уровня только для чтения (CF6; макеты F6 «Preview · what a lane gets» и
 * `member-workout-mobile.png`): заголовок сета мелкими прописными, строка — «4×100 free · 2-3V» и
 * справа время выхода уровня. Вход — уже версия уровня (`workoutForLevel` / `level_workout`
 * сервера): в ячейках только этот уровень. Тексты тренера на иврите — `dir="auto"`.
 *
 * Общий для превью тренера и блока своей дорожки участника.
 */
function WorkoutLevelView({ workout, levelId, hiddenSets = [] }: {
  workout: Workout;
  /** Уровень версии; null — дорожка без уровня (времён нет). */
  levelId: number | null;
  /** Названия сетов, которых у уровня нет, — строка «Set 3 is not for this level» (превью тренера). */
  hiddenSets?: string[];
}) {
  const key = levelId == null ? null : String(levelId);
  return (
    <div className="flex min-w-0 flex-col gap-2.5">
      {workout.note && (
        <p dir="auto" className="m-0 whitespace-pre-line text-[12.5px] font-bold" style={{ color: 'var(--t-text-2)' }}>{workout.note}</p>
      )}
      {workout.sets.map((set, si) => (
        <div key={si} className="flex min-w-0 flex-col gap-1">
          {(set.title || set.repeat > 1) && (
            <div dir="auto" className="text-[10.5px] font-extrabold uppercase tracking-[.06em]" style={{ color: 'var(--t-text-3)' }}>
              {set.repeat > 1 && <span className="hp-mono">{set.repeat}× </span>}
              {set.title}
            </div>
          )}
          {set.lines.map((line, li) => {
            const cell = key != null ? line.per_level?.[key] : undefined;
            return (
              <div key={li} className="flex min-w-0 items-baseline justify-between gap-3 text-[12.5px]">
                <span className="min-w-0 break-words font-bold" style={{ color: 'var(--deep-text)' }}><WorkoutLineLabel line={line} reps={cell?.reps ?? line.reps} /></span>
                {cell?.send_off && (
                  <span className="hp-mono flex-none font-extrabold" style={{ color: 'var(--deep-text)' }}>{cell.send_off}</span>
                )}
              </div>
            );
          })}
        </div>
      ))}
      {hiddenSets.map((title) => (
        <div key={title} dir="auto" className="text-[10.5px] font-extrabold uppercase tracking-[.06em]" style={{ color: 'var(--deep-text-mute)' }}>
          {title} is not for this level
        </div>
      ))}
    </div>
  );
}

export default WorkoutLevelView;
