import React from 'react';
import type { SessionWorkout } from './use-session-workout';
import type { WorkoutLevel } from './workout-level-tag';
import { kmRange, levelMeters } from './workout-totals';

/**
 * Полоса Workout над бассейном рабочего стола (CF6, макет F2): состав («5 sets»), разброс
 * километража по уровням и вход в редактор. Плана на дату нет — подсказка: workout живёт при плане.
 */
function WorkoutStrip({ ws, levels, onOpen }: { ws: SessionWorkout; levels: WorkoutLevel[]; onOpen: () => void }) {
  if (ws.state === 'loading' || ws.state === 'error') return null;

  const count = ws.workout.sets.length;
  const range = count > 0
    ? kmRange(levels.length > 0 ? levels.map((l) => levelMeters(ws.workout, l.id)) : [levelMeters(ws.workout, null)])
    : null;
  const none = ws.state === 'none';

  return (
    <div
      className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-2 rounded-[12px] px-3 py-2.5"
      style={{ border: '1px solid var(--deep-ow-border)', background: 'var(--deep-ow-chip)' }}
    >
      <span className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--deep-ow)' }}>Workout</span>
      <span className="min-w-0 flex-1 text-[12.5px] font-bold" style={{ color: none || count === 0 ? 'var(--t-text-3)' : 'var(--deep-text)' }}>
        {none ? 'Create the lane plan below to add a workout.'
          : count === 0 ? 'Not written yet — one plan for all levels.'
            : <><span className="font-extrabold">{count} {count === 1 ? 'set' : 'sets'}</span>{range && <> · <span className="hp-mono font-extrabold">{range}</span></>}</>}
      </span>
      {!none && (
        <button
          type="button"
          onClick={onOpen}
          className="h-8 flex-none cursor-pointer rounded-[9px] border-0 px-3 text-[12px] font-extrabold"
          style={{ background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }}
        >
          {count === 0 ? 'Write workout' : 'Edit workout ›'}
        </button>
      )}
    </div>
  );
}

export default WorkoutStrip;
