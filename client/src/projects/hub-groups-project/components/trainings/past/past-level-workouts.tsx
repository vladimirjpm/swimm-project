import React, { useState } from 'react';
import type { TrainingRsvp } from '../../../types';
import { useMyLaneNo, YourWorkout } from '../next/lane-block';
import { LevelTag } from '../workout/workout-level-tag';
import { formatKm, levelMeters } from '../workout/workout-totals';
import { lanesLabel, pastLevelWorkouts, type PastLevelWorkout } from './past-workout-levels';

/**
 * Workout прошедшего занятия — всех уровней (решение Влада 26.11.2026). Карточка на уровень
 * (`past-workout-levels.ts`); у каждой — Print (`PrintButton` внутри `YourWorkout`). Свой уровень —
 * первым и раскрыт; чужие — свёрнуты строкой «Lanes 3, 4 · Slow · 1.8 km», раскрываются по клику:
 * четыре уровня по три сета разом — это полтора экрана прокрутки на телефоне.
 * Своей дорожки нет (не был в плане) — раскрыт первый уровень.
 */
function PastLevelWorkouts({ r }: { r: TrainingRsvp }) {
  const lanes = r.lane_view?.source === 'plan' ? r.lane_view.lanes : [];
  const myNo = useMyLaneNo(lanes);
  const list = pastLevelWorkouts(lanes, myNo);
  const [open, setOpen] = useState<ReadonlySet<string>>(() => new Set(list[0] ? [list[0].key] : []));
  if (list.length === 0) return null;

  const toggle = (key: string) => setOpen((prev) => {
    const next = new Set(prev);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    return next;
  });

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
        Workout by level
      </div>
      {list.map((item) => (
        <LevelWorkoutRow key={item.key} item={item} open={open.has(item.key)} onToggle={() => toggle(item.key)} />
      ))}
    </div>
  );
}

function LevelWorkoutRow({ item, open, onToggle }: { item: PastLevelWorkout; open: boolean; onToggle: () => void }) {
  const { lane } = item;
  const levelId = lane.level?.id ?? null;
  const meters = lane.level_workout ? levelMeters(lane.level_workout, levelId) : 0;

  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        className="flex min-w-0 cursor-pointer items-center justify-between gap-3 rounded-[10px] border px-3 py-2 text-left"
        style={{ borderColor: 'var(--deep-card-border)', background: 'transparent', color: 'var(--deep-text)' }}
      >
        <span className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
          <span className="hp-mono text-[12px] font-extrabold" style={{ color: 'var(--deep-pool-label)' }}>{lanesLabel(item.laneNos)}</span>
          {lane.level ? <LevelTag level={lane.level} full /> : (
            <span className="text-[12px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>No level</span>
          )}
          {item.mine && <span className="text-[10px] font-extrabold" style={{ color: 'var(--deep-accent)' }}>YOU</span>}
        </span>
        <span className="flex flex-none items-center gap-2 text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          <span className="hp-mono text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{formatKm(meters)}</span>
          <span aria-hidden="true">{open ? '▴' : '▾'}</span>
        </span>
      </button>
      {open && <YourWorkout lane={lane} title={item.mine ? 'Your workout' : 'Workout'} />}
    </div>
  );
}

export default PastLevelWorkouts;
