import React from 'react';
import type { CoverLayout } from '../cover-data';

const LAYOUTS: { key: CoverLayout; hint: string }[] = [
  { key: 'A', hint: 'Result as one big line' },
  { key: 'B', hint: 'Result on the left, photo on the right' },
  { key: 'C', hint: 'Competition column, result stacked' },
];

const ON: React.CSSProperties = { borderColor: 'var(--t-accent)', background: 'var(--t-accent-soft)', color: 'var(--t-accent)' };
const OFF: React.CSSProperties = { borderColor: 'var(--t-border)', background: 'transparent', color: 'var(--t-text-2)' };

/** Выбор раскладки обложки: три кнопки-переключателя; подпись под буквой — только с 640px. */
export default function LayoutPicker({ value, onChange }: { value: CoverLayout; onChange: (layout: CoverLayout) => void }) {
  return (
    <div className="grid grid-cols-3 gap-2" role="radiogroup" aria-label="Cover layout">
      {LAYOUTS.map((l) => (
        <button
          key={l.key}
          type="button"
          role="radio"
          aria-checked={l.key === value}
          title={l.hint}
          onClick={() => onChange(l.key)}
          className="flex min-h-11 cursor-pointer flex-col items-center justify-center rounded-[10px] border px-2 py-1.5"
          style={l.key === value ? ON : OFF}
        >
          <span className="text-[14px] font-black leading-tight">{l.key}</span>
          <span className="hidden text-[10.5px] font-bold leading-tight sm:block">{l.hint}</span>
        </button>
      ))}
    </div>
  );
}
