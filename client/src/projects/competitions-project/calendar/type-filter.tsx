import React from 'react';
import { IconLotus, IconSquaresFour, IconSwim, IconWaves } from './calendar-icons';
import { MEET_TYPES } from './calendar-state';
import type { MeetType } from './calendar-types';

/*
 * Фильтр по типу: All · Pool · Open water · Artistic — иконка, подпись, число meets.
 * Мульти-выбор, All сбрасывает. Выбран — рамка accent + `--t-accent-soft`.
 * Вид: chips — строка чипов с переносом (сайдбар десктопа); tiles — 4 плитки в ряд (панель Type
 * на телефоне). Правило выбора одно — `toggleMeetType`.
 */

type TypeKey = MeetType | 'all';

// label — полное имя (aria, title); chip — подпись чипа сайдбара; tile — плитки телефона (узко).
const TYPE_OPTIONS: { value: TypeKey; label: string; chip: string; tile: string; Icon: typeof IconSwim }[] = [
  { value: 'all', label: 'All', chip: 'All', tile: 'All', Icon: IconSquaresFour },
  { value: 'pool', label: 'Pool swimming', chip: 'Pool', tile: 'Pool', Icon: IconSwim },
  { value: 'open_water', label: 'Open water', chip: 'Open water', tile: 'Open w.', Icon: IconWaves },
  { value: 'artistic', label: 'Artistic swimming', chip: 'Artistic', tile: 'Artistic', Icon: IconLotus },
];

/** Новый фильтр после клика по типу: All — сброс, остальные — переключатель, порядок как в MEET_TYPES. */
export function toggleMeetType(filter: MeetType[], value: TypeKey): MeetType[] {
  if (value === 'all') return [];
  return filter.includes(value)
    ? filter.filter((t) => t !== value)
    : MEET_TYPES.filter((t) => t === value || filter.includes(t));
}

export interface TypeFilterProps {
  typeFilter: MeetType[];
  onTypeFilter: (next: MeetType[]) => void;
  counts: Record<TypeKey, number>;
  variant: 'chips' | 'tiles';
  /** Плитки в свёрнутой панели не должны ловить Tab. */
  focusable?: boolean;
  className?: string;
}

function TypeFilter({ typeFilter, onTypeFilter, counts, variant, focusable = true, className }: TypeFilterProps) {
  const tiles = variant === 'tiles';
  return (
    <div
      role="group"
      aria-label="Meet type"
      className={`${tiles ? 'grid grid-cols-4 gap-[6px]' : 'flex flex-wrap gap-1.5'} ${className ?? ''}`}
    >
      {TYPE_OPTIONS.map(({ value, label, chip, tile, Icon }) => {
        const selected = value === 'all' ? typeFilter.length === 0 : typeFilter.includes(value);
        return (
          <button
            key={value}
            type="button"
            tabIndex={focusable ? 0 : -1}
            aria-pressed={selected}
            aria-label={`${label}, ${counts[value]}`}
            title={label}
            onClick={() => onTypeFilter(toggleMeetType(typeFilter, value))}
            className={`cc-focus flex cursor-pointer items-center rounded-[10px] border text-[var(--t-text)] hover:border-[var(--t-accent)] ${
              tiles ? 'flex-col gap-[3px] px-1 py-2' : 'h-[34px] gap-1.5 whitespace-nowrap px-2.5'
            } ${
              selected
                ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)]'
                : 'border-[var(--t-border)] bg-[var(--t-surface)]'
            }`}
          >
            <Icon size={16} />
            <span className={tiles ? 'text-[11px] font-semibold' : 'text-[13px] font-semibold'}>
              {tiles ? tile : chip}
            </span>
            <span className={tiles ? 'text-[10px] text-[var(--t-text-2)]' : 'text-[12px] text-[var(--t-text-2)]'}>
              {counts[value]}
            </span>
          </button>
        );
      })}
    </div>
  );
}

export default TypeFilter;
