import React from 'react';
import { dayAriaLabel, dayStatus, meetsOnDay, type MeetStatusKind } from './calendar-model';
import type { CalendarMeet } from './calendar-types';
import { DayResultsCorner, DayStatusMark } from './status-mark';

/*
 * Сетка дней: шапка S M T W T F S + недели ячеек «число + метка статуса». Один блок на мини-месяц
 * сайдбара десктопа (size sm, ячейка 40px) и неделю/месяц телефона (size md, 52px) — вызывающий
 * передаёт, какие недели рисовать (`monthGrid` целиком или одну).
 *
 * Метка — по лучшему статусу стартов дня (хендофф competitions_home-4, 10c: RES / SL / … / точка,
 * `dayStatus`), у дня с протоколом ещё угол-флажок. Чемпионат виден золотой полосой ячейки — бейджа
 * группы в ячейке больше нет (в 36px не помещаются оба).
 *
 * Ячейка (хендофф competitions_home-3, 2b): день с чемпионатом — золотая полоса
 * (`--t-champ-strip-*`); сегодня — рамка `--t-accent-border` и акцентное число; выбранный —
 * фон `--t-accent-soft` + рамка `--t-accent`. Рамка 1px есть у всех (прозрачная), чтобы выбор не
 * двигал вёрстку. Прошедшие — opacity .88, не .6 из макета: замер контраста Б9.
 */

const LETTERS = ['S', 'M', 'T', 'W', 'T', 'F', 'S'];

const CELL_H = { sm: 'h-[40px]', md: 'h-[52px]' } as const;

const STATUS_ARIA: Record<MeetStatusKind, string> = {
  results: ', results',
  startlist: ', start list',
  pending: ', results soon',
  upcoming: '',
};

function DayCell({
  iso,
  meets,
  today,
  selected,
  onSelect,
  size,
}: {
  iso: string;
  meets: CalendarMeet[];
  today: string;
  selected: boolean;
  onSelect: (iso: string) => void;
  size: 'sm' | 'md';
}) {
  const isToday = iso === today;
  const champ = meets.some((m) => m.is_championship);
  const { status, hasResults } = dayStatus(meets, iso, today);

  const border = selected
    ? 'border-[var(--t-accent)]'
    : isToday
      ? 'border-[var(--t-accent-border)]'
      : champ
        ? 'border-[var(--t-champ-strip-border)]'
        : 'border-transparent';
  const bg = selected
    ? 'bg-[var(--t-accent-soft)]'
    : champ
      ? 'bg-[var(--t-champ-strip-bg)]'
      : 'bg-transparent hover:bg-[var(--t-nested)]';

  return (
    <button
      type="button"
      aria-label={`${dayAriaLabel(iso, meets.length)}${STATUS_ARIA[status ?? 'upcoming']}`}
      aria-pressed={selected}
      onClick={() => onSelect(iso)}
      className={`cc-focus relative flex ${CELL_H[size]} min-w-0 cursor-pointer overflow-hidden flex-col items-center justify-center rounded-[8px] border p-0 ${
        size === 'sm' ? 'gap-[3px]' : 'gap-1'
      } ${border} ${bg} ${iso < today ? 'opacity-[.88]' : ''}`}
    >
      <span
        className={`text-[12px] leading-none ${
          isToday ? 'font-bold text-[var(--t-accent-text)]' : 'font-medium text-[var(--t-text)]'
        }`}
      >
        {Number(iso.slice(8))}
      </span>
      {hasResults && <DayResultsCorner />}
      <span aria-hidden="true" className={`flex items-center justify-center ${size === 'sm' ? 'h-[15px]' : 'h-[18px]'}`}>
        {status && <DayStatusMark status={status} />}
      </span>
    </button>
  );
}

export interface DayGridProps {
  /** Недели сетки месяца (`monthGrid`), клетка вне месяца — null. */
  weeks: (string | null)[][];
  /** Meets, из которых берутся метки дня (уже после фильтра по типу). */
  meets: CalendarMeet[];
  today: string;
  selectedDay: string | null;
  onSelectDay: (iso: string) => void;
  size?: 'sm' | 'md';
}

function DayGrid({ weeks, meets, today, selectedDay, onSelectDay, size = 'md' }: DayGridProps) {
  const gap = size === 'sm' ? 'gap-[2px]' : 'gap-1';
  return (
    <div className="flex flex-col gap-[6px]">
      <div className={`grid grid-cols-7 ${gap}`} aria-hidden="true">
        {LETTERS.map((l, i) => (
          <span key={i} className="text-center text-[10px] uppercase tracking-[.06em] text-[var(--t-text-2)]">
            {l}
          </span>
        ))}
      </div>
      <div className={`grid grid-cols-7 ${gap}`}>
        {weeks.flatMap((week, wi) =>
          week.map((iso, col) =>
            iso ? (
              <DayCell
                key={iso}
                iso={iso}
                meets={meetsOnDay(meets, iso)}
                today={today}
                selected={iso === selectedDay}
                onSelect={onSelectDay}
                size={size}
              />
            ) : (
              <div key={`blank-${wi}-${col}`} aria-hidden="true" className={CELL_H[size]} />
            ),
          ),
        )}
      </div>
    </div>
  );
}

export default DayGrid;
