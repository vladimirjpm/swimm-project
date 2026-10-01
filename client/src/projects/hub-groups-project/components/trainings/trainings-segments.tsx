import React from 'react';
import { useTrainingsTab, type TrainingsView } from './trainings-context';
import { MarkDot } from './trainings-section';

/**
 * Переключатель видов таба Trainings на телефоне: `Next · Trainings · Lanes` (хендофф 2a), вид
 * в `?view=`. Участнику — акцентный сегмент (`--deep-seg-*`), управляющему — фиолетовая
 * таблетка (`--deep-ow`): у него здесь правка. На сегменте — метка внимания своего вида (Т8).
 * Участнику без расписания и без опубликованных планов Lanes скрыт (`lanesHidden`, Т9).
 * На десктопе переключателя нет — две колонки.
 */

const SEGMENTS: { id: TrainingsView; label: string }[] = [
  { id: 'next', label: 'Next' },
  { id: 'trainings', label: 'Trainings' },
  { id: 'lanes', label: 'Lanes' },
];

function TrainingsSegments() {
  const { view, setView, manages, marks, lanesHidden } = useTrainingsTab();
  return (
    <div className="deep-panel-row mb-3">
      <div
        className="flex gap-0.5 rounded-[12px] p-[3px]"
        style={{ background: 'var(--deep-seg-track)' }}
        role="group"
        aria-label="Trainings view"
      >
        {SEGMENTS.filter(({ id }) => !(id === 'lanes' && lanesHidden)).map(({ id, label }) => {
          const active = view === id;
          const mark = marks[id];
          return (
            <button
              key={id}
              type="button"
              aria-pressed={active}
              onClick={() => setView(id)}
              className="relative flex min-h-[40px] flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-[10px] border-0 px-2 text-[13px] font-extrabold"
              style={active
                ? manages
                  ? { background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }
                  : { background: 'var(--deep-seg-active-bg)', color: 'var(--deep-seg-active-fg)' }
                : { background: 'transparent', color: 'var(--deep-text-mute)' }}
            >
              {label}
              {mark && <MarkDot mark={mark} />}
            </button>
          );
        })}
      </div>
    </div>
  );
}

export default TrainingsSegments;
