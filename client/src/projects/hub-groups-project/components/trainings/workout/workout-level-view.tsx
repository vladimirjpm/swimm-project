import React from 'react';
import type { Workout, WorkoutSet } from '../../../types';
import WorkoutLineLabel from './workout-line-label';
import { lineDir } from './workout-format';
import { formatMeters, setMeters } from './workout-totals';

/**
 * Workout ОДНОГО уровня только для чтения (CF6; макеты F6 «Preview · what a lane gets» и
 * `member-workout-mobile.png`). Вход — уже версия уровня (`workoutForLevel` / `level_workout`
 * сервера): в ячейках только этот уровень, у каждого сета есть название (своё или «Set N» по номеру
 * у тренера).
 *
 * Сеты — отдельные блоки (Влад, 15.11.2026: «всё скученно, не видно сетов»): шапка сета — название
 * и его метры («Set 1 · 2000 m», «2× Set 2»), между сетами — черта. Строка: «4×100 free · 2-3V» и
 * время выхода уровня в своей колонке; ивритская строка — справа налево, время слева (`lineDir`).
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
  return (
    <div className="flex min-w-0 flex-col">
      {workout.note && (
        <p dir="auto" className="m-0 mb-2.5 whitespace-pre-line text-[12.5px] font-bold" style={{ color: 'var(--t-text-2)' }}>{workout.note}</p>
      )}
      {workout.sets.map((set, si) => (
        <SetBlock key={si} set={set} levelId={levelId} first={si === 0} />
      ))}
      {hiddenSets.map((title) => (
        <div
          key={title}
          dir="auto"
          className="mt-2.5 border-t pt-2 text-[10.5px] font-extrabold uppercase tracking-[.06em]"
          style={{ color: 'var(--deep-text-mute)', borderColor: 'var(--deep-divider)' }}
        >
          {title} is not for this level
        </div>
      ))}
    </div>
  );
}

function SetBlock({ set, levelId, first }: { set: WorkoutSet; levelId: number | null; first: boolean }) {
  const key = levelId == null ? null : String(levelId);
  const meters = setMeters(set, levelId);
  return (
    <section
      className={first ? 'flex min-w-0 flex-col gap-1.5' : 'mt-2.5 flex min-w-0 flex-col gap-1.5 border-t pt-2.5'}
      style={first ? undefined : { borderColor: 'var(--deep-divider)' }}
    >
      <header className="flex items-baseline justify-between gap-3">
        <span className="flex min-w-0 items-baseline gap-1.5">
          <span dir="auto" className="min-w-0 truncate text-[12px] font-extrabold" style={{ color: 'var(--deep-accent)' }} title={set.title ?? undefined}>
            {set.repeat > 1 && <bdi dir="ltr" className="hp-mono">{set.repeat}× </bdi>}
            {set.title}
          </span>
          {set.main && (
            <span
              className="flex-none rounded-full border px-1.5 text-[9.5px] font-extrabold uppercase leading-[15px] tracking-[.07em]"
              style={{ color: 'var(--deep-accent)', borderColor: 'var(--deep-accent)' }}
            >
              Main
            </span>
          )}
        </span>
        {meters > 0 && (
          <span className="hp-mono flex-none text-[11px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>{formatMeters(meters)}</span>
        )}
      </header>
      {set.lines.map((line, li) => {
        const cell = key != null ? line.per_level?.[key] : undefined;
        return (
          <div key={li} dir={lineDir(line.text)} className="flex min-w-0 items-baseline justify-between gap-3 text-[13px] leading-snug">
            <span className="min-w-0 break-words font-bold" style={{ color: 'var(--deep-text)' }}>
              <WorkoutLineLabel line={line} reps={cell?.reps ?? line.reps} />
            </span>
            {cell?.send_off && (
              <span className="hp-mono flex-none font-extrabold" style={{ color: 'var(--deep-text)' }}>{cell.send_off}</span>
            )}
          </div>
        );
      })}
    </section>
  );
}

export default WorkoutLevelView;
