import React from 'react';
import { useTrainingsTab, type TrainingsView } from './trainings-context';
import { MarkDot } from './trainings-section';
import type { TrainingMark } from './use-training-marks';

/**
 * Переключатель видов таба Trainings на телефоне: `Next · Trainings · Lanes` (хендофф 2a), вид
 * в `?view=`. Участнику — акцентный сегмент (`--deep-seg-*`). На сегменте — метка внимания своего
 * вида (Т8). Участнику без расписания и без опубликованных планов Lanes скрыт (`lanesHidden`, Т9).
 * На десктопе переключателя нет — две колонки.
 *
 * У тренера этих видов нет (docs/plans/trainings-coach-flow-plan.md, CF1): он видит список занятий
 * и рабочий стол. Сам ВИД сегментов — `SegmentBar` — общий: рабочий стол тренера рисует им
 * `People · Pool` фиолетовой таблеткой (`--deep-ow`: у него здесь правка).
 */

export interface SegmentOption<T extends string> {
  id: T;
  label: string;
  mark?: TrainingMark | null;
}

/** Полоса сегментов: один вид на переключатель видов участника и `People · Pool` тренера. */
export function SegmentBar<T extends string>({
  options, value, onChange, coach = false, label,
}: {
  options: SegmentOption<T>[];
  value: T;
  onChange: (id: T) => void;
  /** Тон тренера: активный сегмент — `--deep-ow`. */
  coach?: boolean;
  /** `aria-label` группы. */
  label: string;
}) {
  return (
    <div
      className="flex gap-0.5 rounded-[12px] p-[3px]"
      style={{ background: 'var(--deep-seg-track)' }}
      role="group"
      aria-label={label}
    >
      {options.map(({ id, label: text, mark }) => {
        const active = value === id;
        return (
          <button
            key={id}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(id)}
            className="relative flex min-h-[40px] flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-[10px] border-0 px-2 text-[13px] font-extrabold"
            style={active
              ? coach
                ? { background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }
                : { background: 'var(--deep-seg-active-bg)', color: 'var(--deep-seg-active-fg)' }
              : { background: 'transparent', color: 'var(--deep-text-mute)' }}
          >
            {text}
            {mark && <MarkDot mark={mark} />}
          </button>
        );
      })}
    </div>
  );
}

const SEGMENTS: { id: TrainingsView; label: string }[] = [
  { id: 'next', label: 'Next' },
  { id: 'trainings', label: 'Trainings' },
  { id: 'lanes', label: 'Lanes' },
];

function TrainingsSegments() {
  const { view, setView, marks, lanesHidden } = useTrainingsTab();
  return (
    <div className="deep-panel-row mb-3">
      <SegmentBar
        label="Trainings view"
        value={view}
        onChange={setView}
        options={SEGMENTS
          .filter(({ id }) => !(id === 'lanes' && lanesHidden))
          .map(({ id, label }) => ({ id, label, mark: marks[id] }))}
      />
    </div>
  );
}

export default TrainingsSegments;
