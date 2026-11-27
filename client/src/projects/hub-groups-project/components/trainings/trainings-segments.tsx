import React from 'react';
import { useTrainingsTab, type TrainingsView } from './trainings-context';
import { MarkDot } from './trainings-section';
import type { TrainingMark, TrainingMarks } from './use-training-marks';

/**
 * Переключатель видов таба Trainings на телефоне: `Next · Past · Trainings · Lanes` (хендофф 2a;
 * Past — 27.11.2026), вид в `?view=`. Участнику — акцентный сегмент (`--deep-seg-*`). На сегменте — метка внимания своего
 * вида (Т8). Участнику без расписания и без опубликованных планов Lanes скрыт (`lanesHidden`, Т9).
 * На десктопе — две колонки; над левой — `Next · Past` (`NextPastSegments`).
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
  { id: 'past', label: 'Past' },
  { id: 'trainings', label: 'Trainings' },
  { id: 'lanes', label: 'Lanes' },
];

/** Метка вида; у Past меток нет (прошедшее ничего от зрителя не ждёт). */
function markOf(marks: TrainingMarks, id: TrainingsView): TrainingMark | null {
  return id === 'past' ? null : marks[id];
}

/**
 * Сегменты на телефоне: `Next · Past · Trainings · Lanes`. Past (27.11.2026) скрыт, когда прошедших
 * с планом нет (`pastHidden`), Lanes — по `lanesHidden` (Т9).
 */
function TrainingsSegments() {
  const { view, setView, marks, lanesHidden, pastHidden } = useTrainingsTab();
  return (
    <div className="deep-panel-row mb-3">
      <SegmentBar
        label="Trainings view"
        value={view}
        onChange={setView}
        options={SEGMENTS
          .filter(({ id }) => !(id === 'lanes' && lanesHidden) && !(id === 'past' && pastHidden))
          .map(({ id, label }) => ({ id, label, mark: markOf(marks, id) }))}
      />
    </div>
  );
}

/**
 * Десктоп: над левой колонкой — `Next · Past` (27.11.2026). Trainings — правая колонка, Lanes — ссылкой
 * из Next, поэтому сегментов здесь два. Прошедших с планом нет — переключателя нет вовсе.
 */
export function NextPastSegments() {
  const { view, setView, marks, pastHidden } = useTrainingsTab();
  if (pastHidden) return null;
  return (
    <SegmentBar
      label="Next or past trainings"
      value={view === 'past' ? 'past' : 'next'}
      onChange={setView}
      options={[
        { id: 'next', label: 'Next', mark: marks.next },
        { id: 'past', label: 'Past' },
      ]}
    />
  );
}

export default TrainingsSegments;
