import React from 'react';
import { Check } from '../../print/dialog/photo-controls';
import type { PosterSwimSource } from '../poster-data';
import { swimChoiceLabel } from '../poster-source';

/**
 * Список заплывов с галочками: какие идут на плакат. Когда отмечено столько, сколько помещается
 * в раскладку, неотмеченные галочки гаснут — сперва нужно снять другую.
 */
export default function SwimPicker({ swims, selected, full, limit, onToggle }: {
  swims: PosterSwimSource[];
  selected: ReadonlySet<number>;
  /** Отмечено столько, сколько помещается. */
  full: boolean;
  limit: number;
  onToggle: (id: number) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <div className="grid grid-cols-1 gap-x-4 sm:grid-cols-2">
        {swims.map((swim) => (
          <Check
            key={swim.id}
            label={<span className="hp-mono">{swimChoiceLabel(swim)}</span>}
            checked={selected.has(swim.id)}
            disabled={full && !selected.has(swim.id)}
            onChange={() => onToggle(swim.id)}
          />
        ))}
      </div>
      <p className="m-0 text-[11px] font-bold text-[var(--t-text-3)]">
        {selected.size} of {swims.length} on the poster · this layout fits up to {limit}
      </p>
    </div>
  );
}
