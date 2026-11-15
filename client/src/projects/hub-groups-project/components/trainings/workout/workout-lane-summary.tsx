import React from 'react';
import type { Workout } from '../../../types';
import { setSwumBy } from './workout-for-level';
import { formatKm, levelMeters } from './workout-totals';

/**
 * Краткая сводка workout в карточке дорожки редактора (CF6, вместо прежнего поля задания):
 * километры версии уровня дорожки, число её сетов и их названия. Колонка дорожки узкая (112px на
 * десктопе) — три короткие строки, названия сетов тренера изолированы (`bdi`: иврит в английском).
 */
function WorkoutLaneSummary({ workout, levelId }: { workout: Workout; levelId: number | null }) {
  const sets = workout.sets
    .map((set, i) => ({ set, i }))
    .filter(({ set }) => setSwumBy(set, levelId))
    .map(({ set, i }) => set.title?.trim() || `Set ${i + 1}`);
  return (
    <div
      className="flex min-w-0 flex-col gap-0.5 rounded-[8px] px-2 py-1.5"
      style={{ background: 'var(--deep-card-bg)', border: '1px solid var(--deep-card-border)' }}
      title={sets.join(' · ')}
    >
      <span className="text-[9.5px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>Workout</span>
      <span className="text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        <span className="hp-mono font-extrabold" style={{ color: 'var(--deep-text)' }}>{formatKm(levelMeters(workout, levelId))}</span>
        {' · '}{sets.length} {sets.length === 1 ? 'set' : 'sets'}
      </span>
      {sets.length > 0 && (
        <span className="line-clamp-2 text-[10.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          {sets.map((title, i) => <React.Fragment key={i}>{i > 0 && ' · '}<bdi>{title}</bdi></React.Fragment>)}
        </span>
      )}
    </div>
  );
}

export default WorkoutLaneSummary;
