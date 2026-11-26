import React, { useState } from 'react';
import type { LanePlan, Workout } from '../../../types';
import { useTrainingsTab } from '../trainings-context';
import { SegmentBar } from '../trainings-segments';
import { PrintButton } from './print/print-button';
import { levelAbbr } from './workout-format';
import { setSwumBy, workoutForLevel } from './workout-for-level';
import type { WorkoutLevel } from './workout-level-tag';
import WorkoutLevelView from './workout-level-view';
import { formatKm, levelMeters } from './workout-totals';

/**
 * Превью «что получит дорожка» (CF6, макет F6 справа): выбор уровня сегментом, версия уровня
 * (`workoutForLevel`) только для чтения, итог в км и какие дорожки плана её получат.
 */
function WorkoutPreview({ workout, levels, plan }: {
  workout: Workout;
  levels: WorkoutLevel[];
  plan: LanePlan | null;
}) {
  const { group } = useTrainingsTab();
  const [picked, setPicked] = useState<number | null>(null);
  const levelId = picked != null && levels.some((l) => l.id === picked) ? picked : levels[0]?.id ?? null;
  const level = levels.find((l) => l.id === levelId) ?? null;
  const version = workoutForLevel(workout, levelId);
  const hidden = workout.sets
    .map((set, i) => ({ set, i }))
    .filter(({ set }) => !setSwumBy(set, levelId))
    .map(({ set, i }) => set.title?.trim() || `Set ${i + 1}`);
  const lanes = plan?.lanes.filter((l) => l.level?.id === levelId).map((l) => l.lane_no) ?? [];

  return (
    <div className="flex min-w-0 flex-col gap-3 rounded-[14px] p-3.5" style={{ background: 'var(--deep-card-bg-row)', border: '1px solid var(--deep-card-border)' }}>
      <div className="flex items-center justify-between gap-3">
        <span className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
          Preview · what a lane gets
        </span>
        {version && (
          <PrintButton
            source={{
              workout: version,
              levelId,
              level: level ? { rank: level.rank, name: level.name } : null,
              groupName: group.name,
              // Номера сетов — как у тренера: уровень, пропускающий сет, видит дырку в нумерации (план §3.2).
              setNumbers: workout.sets.map((_, i) => i + 1).filter((_, i) => setSwumBy(workout.sets[i], levelId)),
            }}
          />
        )}
      </div>
      {levels.length > 1 && (
        <SegmentBar
          label="Level to preview"
          coach
          value={String(levelId)}
          onChange={(id) => setPicked(Number(id))}
          options={levels.map((l) => ({ id: String(l.id), label: levelAbbr(l.name) }))}
        />
      )}
      {version ? <WorkoutLevelView workout={version} levelId={levelId} hiddenSets={hidden} /> : (
        <p className="m-0 text-[12.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>Nothing for this level yet.</p>
      )}
      <div className="flex items-baseline justify-between gap-3 border-t pt-2" style={{ borderColor: 'var(--deep-divider)' }}>
        <span className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>Total</span>
        <span className="hp-mono text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{formatKm(levelMeters(workout, levelId))}</span>
      </div>
      {level && (
        <p dir="auto" className="m-0 text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          {lanes.length === 0 ? `No lane is ${level.name} in the plan yet.`
            : lanes.length === 1 ? `Lane ${lanes[0]} is ${level.name} and gets this version.`
              : `Lanes ${lanes.join(', ')} are ${level.name} and get this version.`}
        </p>
      )}
    </div>
  );
}

export default WorkoutPreview;
