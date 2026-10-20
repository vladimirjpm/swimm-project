import React from 'react';
import type { CalendarView } from './calendar-state';

/*
 * Сегмент видов календаря: radiogroup с roving tabindex (Tab попадает только на выбранный вид),
 * ←/→ (и ↑/↓) переключают вид, пропуская disabled; Home/End — крайние. Disabled-вид остаётся
 * в порядке Tab (aria-disabled), но клик по нему ничего не делает.
 * Набор видов и подписи — от вызывающего: десктоп 9a — Year · Month, телефон — Year · Month · Meet.
 * Своя вёрстка, не `.deep-seg`: макету нужна обводка `--t-divider` с разделителями
 * (docs/ui-components.md §6).
 */

export interface ViewOption {
  value: CalendarView;
  label: string;
}

export interface ViewSegmentProps {
  options: ViewOption[];
  view: CalendarView;
  onView: (view: CalendarView) => void;
  disabledViews: CalendarView[];
  /** stretch — опции делят ширину поровну (телефон, сайдбар); inline — по содержимому. */
  layout?: 'stretch' | 'inline';
  className?: string;
}

function ViewSegment({ options, view, onView, disabledViews, layout = 'stretch', className }: ViewSegmentProps) {
  const refs = React.useRef<Record<string, HTMLButtonElement | null>>({});

  const move = (e: React.KeyboardEvent, from: number, step: 1 | -1) => {
    e.preventDefault();
    for (let k = 1; k <= options.length; k++) {
      const next = options[(from + step * k + options.length * 2) % options.length];
      if (disabledViews.includes(next.value)) continue;
      onView(next.value);
      refs.current[next.value]?.focus();
      return;
    }
  };

  const onKeyDown = (e: React.KeyboardEvent, index: number) => {
    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') move(e, index, 1);
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') move(e, index, -1);
    else if (e.key === 'Home') move(e, options.length - 1, 1);
    else if (e.key === 'End') move(e, 0, -1);
  };

  return (
    <div
      role="radiogroup"
      aria-label="Calendar view"
      className={`${layout === 'stretch' ? 'grid' : 'inline-flex'} h-[38px] overflow-hidden rounded-[10px] border border-[var(--t-divider)] ${className ?? ''}`}
      style={layout === 'stretch' ? { gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` } : undefined}
    >
      {options.map((opt, i) => {
        const selected = view === opt.value;
        const disabled = disabledViews.includes(opt.value);
        return (
          <button
            key={opt.value}
            ref={(el) => {
              refs.current[opt.value] = el;
            }}
            type="button"
            role="radio"
            aria-checked={selected}
            aria-disabled={disabled || undefined}
            tabIndex={selected ? 0 : -1}
            onKeyDown={(e) => onKeyDown(e, i)}
            onClick={() => {
              if (!disabled) onView(opt.value);
            }}
            className={`cc-focus cc-focus-inset text-[14px] font-bold ${layout === 'inline' ? 'px-3.5' : ''} ${
              i > 0 ? 'border-l border-solid border-l-[var(--t-divider)]' : ''
            } ${
              selected
                ? 'bg-[var(--deep-seg-active-bg)] text-[var(--deep-seg-active-fg)]'
                : 'bg-transparent text-[var(--t-text)]'
            } ${disabled ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'}`}
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

export default ViewSegment;
