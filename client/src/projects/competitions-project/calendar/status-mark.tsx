import React from 'react';
import { IconFlagCheckered, IconHourglass, IconListNumbers } from './calendar-icons';
import type { MeetStatusKind } from './calendar-model';

/*
 * Статусы старта по хендоффу competitions_home-4 (вариант 10c): одна и та же шкала в двух видах.
 *  • `DayStatusMark` — метка в ячейке дня: RES (заливка `--t-res`), SL (контур `--t-sl`),
 *    «…» (прошёл, протокола нет), точка (впереди, стартлиста нет);
 *  • `DayResultsCorner` — угол-флажок ячейки дня с протоколом;
 *  • `StatusButton` — кнопка внизу куба: залитая Results, контурная Startlist, серая «Results
 *    soon», пунктир «Startlist not yet». Это ВИЗУАЛ кнопки (`span`): ссылка — весь куб, вложенная
 *    ссылка в ссылке невалидна.
 * Цвета — только роли `--t-res` / `--t-res-ink` / `--t-sl` (deep-theme.css). Серые подписи — `--t-text-2`,
 * не `--t-text-3` из макета: в тёмной `--t-text-3` давал 4.44–4.46 (замер 20.10).
 */

const LABEL = 'text-[9px] font-bold leading-none tracking-[.06em] rounded-[4px]';

export function DayStatusMark({ status }: { status: MeetStatusKind }) {
  if (status === 'results') {
    return <span className={`${LABEL} bg-[var(--t-res)] px-[5px] py-[2px] text-[var(--t-res-ink)]`}>RES</span>;
  }
  if (status === 'startlist') {
    return <span className={`${LABEL} border border-[var(--t-sl)] px-1 py-px text-[var(--t-sl)]`}>SL</span>;
  }
  if (status === 'pending') {
    return <span className="text-[11px] font-bold leading-none tracking-[.06em] text-[var(--t-text-2)]">…</span>;
  }
  return <span className="h-[5px] w-[5px] rounded-full bg-[var(--t-text-3)]" />;
}

/** Треугольник 12px в правом верхнем углу ячейки (родитель — `relative`, `overflow-hidden`). */
export function DayResultsCorner() {
  return (
    <span
      aria-hidden="true"
      className="absolute right-0 top-0 h-0 w-0 border-l-[12px] border-t-[12px] border-l-transparent border-t-[var(--t-res)]"
    />
  );
}

const BUTTON = 'flex h-[34px] items-center justify-center gap-[6px] rounded-[8px]';

export function StatusButton({ kind, label }: { kind: MeetStatusKind; label: string }) {
  if (kind === 'results') {
    return (
      <span className={`${BUTTON} bg-[var(--t-res)] text-[13px] font-semibold text-[var(--t-res-ink)]`}>
        <IconFlagCheckered size={15} />
        {label}
      </span>
    );
  }
  if (kind === 'startlist') {
    return (
      <span className={`${BUTTON} border-[1.5px] border-[var(--t-sl)] text-[13px] font-semibold text-[var(--t-sl)]`}>
        <IconListNumbers size={15} />
        {label}
      </span>
    );
  }
  if (kind === 'pending') {
    return (
      <span className={`${BUTTON} bg-[var(--t-border-2)] text-[12px] text-[var(--t-text-2)]`}>
        <IconHourglass size={14} />
        {label}
      </span>
    );
  }
  return (
    <span className={`${BUTTON} border border-dashed border-[var(--t-border)] text-[12px] text-[var(--t-text-2)]`}>
      {label}
    </span>
  );
}
