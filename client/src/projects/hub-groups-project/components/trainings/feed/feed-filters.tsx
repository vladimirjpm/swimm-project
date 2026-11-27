import React from 'react';
import type { TrainingType } from '../../../types';
import { TRAINING_TYPES } from '../training-types';

/**
 * Чипы ленты тренировок: `All · ⏱ Times · ▶ Videos · 📋 Plan` с числом справа (по умолчанию —
 * All; «📋 Plan» — тренировки с планом, который можно открыть, 27.11.2026). Состояние фильтра держит
 * лента (`training-feed.tsx`), в адрес он не пишется. Выбранный чип у участника — акцент,
 * у управляющего — цвет тренера (`--deep-ow-*`), как в прототипе 2a / 3b.
 *
 * Второй ряд — типы тренировок (решение Влада 01.10.2026): только когда в ленте больше одного
 * типа (иначе фильтровать нечего), только присутствующие, с числом. Тип сочетается с первым
 * рядом («Technique» + «▶ Videos»); повторное нажатие на выбранный тип снимает его.
 */

export type FeedFilter = 'all' | 'times' | 'videos' | 'plan';

export interface FeedFilterCounts {
  all: number;
  times: number;
  videos: number;
  /** Тренировки, у которых можно открыть план (27.11.2026, `past/viewable-plans.ts`). */
  plan: number;
}

/**
 * `whenEmpty: false` — чипа нет, пока в ленте нет ни одной такой тренировки (у группы без планов
 * чип «📋 Plan» с нулём только путал бы). Выбранный чип виден всегда — иначе фильтр не снять.
 */
const CHIPS: { id: FeedFilter; label: string; whenEmpty?: false }[] = [
  { id: 'all', label: 'All' },
  { id: 'times', label: '⏱ Times' },
  { id: 'videos', label: '▶ Videos' },
  { id: 'plan', label: '📋 Plan', whenEmpty: false },
];

function FeedFilters({
  value, counts, onChange, types, type, onType, coach, isDesktop,
}: {
  value: FeedFilter;
  counts: FeedFilterCounts;
  onChange: (next: FeedFilter) => void;
  /** Число тренировок каждого типа во всей ленте (без нулей). */
  types: Partial<Record<TrainingType, number>>;
  /** Выбранный тип; null — все. */
  type: TrainingType | null;
  onType: (next: TrainingType | null) => void;
  /** Управляющий — выбранный чип цвета тренера. */
  coach: boolean;
  isDesktop: boolean;
}) {
  const selected: React.CSSProperties = coach
    ? { background: 'var(--deep-ow-chip)', borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' }
    : { background: 'var(--deep-accent-chip)', borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' };
  const chip = (key: string, label: React.ReactNode, count: number, on: boolean, onClick: () => void) => (
    <button
      key={key}
      type="button"
      aria-pressed={on}
      onClick={onClick}
      className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-full border px-3 text-[12px] font-extrabold"
      style={{
        height: isDesktop ? 32 : 34,
        ...(on
          ? selected
          : { background: 'transparent', borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' }),
      }}
    >
      {label}
      <span className="hp-mono text-[10.5px] opacity-75">{count}</span>
    </button>
  );

  const present = TRAINING_TYPES.filter((t) => (types[t.id] ?? 0) > 0);

  return (
    <div className="mb-3 flex flex-col gap-2">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Filter trainings">
        {CHIPS.filter((c) => c.whenEmpty !== false || counts[c.id] > 0 || value === c.id).map((c) => chip(c.id, c.label, counts[c.id], value === c.id, () => onChange(c.id)))}
      </div>
      {present.length > 1 && (
        <div className="flex flex-wrap gap-2" role="group" aria-label="Training type">
          {present.map((t) => chip(
            t.id,
            <><span aria-hidden="true">{t.glyph}</span>{t.label}</>,
            types[t.id] ?? 0,
            type === t.id,
            () => onType(type === t.id ? null : t.id),
          ))}
        </div>
      )}
    </div>
  );
}

export default FeedFilters;
