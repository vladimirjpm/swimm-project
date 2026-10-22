import React from 'react';
import type { FilterZoneMode } from './types';

/**
 * Строка 1 зоны, левая часть: сегмент-контрол «All programme / My plan». Два оттенка —
 * программа акцентом, план цветом избранного (`--theme-personal-*`); поэтому это не
 * `DeepSeg` (см. §6 `docs/ui-components.md`).
 */
export default function ModeSwitch({ mode, planSwims, onChange }: {
  mode: FilterZoneMode;
  /** N в «My plan · N» — сколько всего моих заплывов на старте. 0 — без числа. */
  planSwims: number;
  onChange: (mode: FilterZoneMode) => void;
}) {
  return (
    <div
      className="flex min-w-0 flex-1 gap-0.5 rounded-[12px] p-[3px] sm:flex-none"
      style={{ background: 'var(--deep-seg-track)' }}
      role="tablist"
      aria-label="Start list view"
    >
      <ModeSegment
        active={mode === 'all'}
        onClick={() => onChange('all')}
        label="All programme"
        color="var(--deep-accent)"
      />
      <ModeSegment
        active={mode === 'plan'}
        onClick={() => onChange('plan')}
        label={planSwims > 0 ? `⭐ My plan · ${planSwims}` : '⭐ My plan'}
        color="var(--theme-personal-accent)"
      />
    </div>
  );
}

/** Половина сегмент-контрола: активная — плашка цвета карточки с тенью, неактивная — прозрачная. */
function ModeSegment({ active, onClick, label, color }: {
  active: boolean; onClick: () => void; label: string; color: string;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className="flex-1 whitespace-nowrap rounded-[10px] px-3 py-[7px] text-[12.5px] font-extrabold sm:flex-none"
      style={{
        background: active ? 'var(--deep-card-bg)' : 'transparent',
        boxShadow: active ? 'var(--deep-card-shadow)' : 'none',
        color: active ? color : 'var(--deep-text-mute)',
      }}
    >
      {label}
    </button>
  );
}
