import React from 'react';

/**
 * Чипы ленты тренировок: `All · ⏱ Times · ▶ Videos` с числом справа. Состояние фильтра держит
 * лента (`training-feed.tsx`), в адрес он не пишется. Выбранный чип у участника — акцент,
 * у управляющего — цвет тренера (`--deep-ow-*`), как в прототипе 2a / 3b.
 */

export type FeedFilter = 'all' | 'times' | 'videos';

export interface FeedFilterCounts {
  all: number;
  times: number;
  videos: number;
}

const CHIPS: { id: FeedFilter; label: string }[] = [
  { id: 'all', label: 'All' },
  { id: 'times', label: '⏱ Times' },
  { id: 'videos', label: '▶ Videos' },
];

function FeedFilters({
  value, counts, onChange, coach, isDesktop,
}: {
  value: FeedFilter;
  counts: FeedFilterCounts;
  onChange: (next: FeedFilter) => void;
  /** Управляющий — выбранный чип цвета тренера. */
  coach: boolean;
  isDesktop: boolean;
}) {
  return (
    <div className="mb-3 flex flex-wrap gap-2" role="group" aria-label="Filter trainings">
      {CHIPS.map((chip) => {
        const on = value === chip.id;
        const selected: React.CSSProperties = coach
          ? { background: 'var(--deep-ow-chip)', borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' }
          : { background: 'var(--deep-accent-chip)', borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' };
        return (
          <button
            key={chip.id}
            type="button"
            aria-pressed={on}
            onClick={() => onChange(chip.id)}
            className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-full border px-3 text-[12px] font-extrabold"
            style={{
              height: isDesktop ? 32 : 34,
              ...(on
                ? selected
                : { background: 'transparent', borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' }),
            }}
          >
            {chip.label}
            <span className="hp-mono text-[10.5px] opacity-75">{counts[chip.id]}</span>
          </button>
        );
      })}
    </div>
  );
}

export default FeedFilters;
