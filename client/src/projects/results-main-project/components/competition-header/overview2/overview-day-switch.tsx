import React from 'react';
import type { OverviewDay } from '../types';

// Переключатель «All / Day 1 / Day 2» над мозаикой Overview (docs/plans/official-awards-plan.md
// §5.6). Только у многодневного события: «All» — обзор события (?eventId=), «Day N» — обзор
// одного дня (?competitionId=<id дня>), сервер для этого готов. Выбор — ВИД, поэтому живёт в
// query (?day=N), а не в пути (правило routes.ts). Тот же ?day= стоит над табом Clubs (зачёт
// дня); Swims, Records, Media и Start list переключатель не трогает.
//
// Своя вёрстка: общего чипа-фильтра на тем-токенах results-страницы в реестре нет
// (docs/ui-components.md §6).

interface Props {
  days: OverviewDay[];
  /** Номер выбранного дня; null — «All». */
  value: number | null;
  onChange(day: number | null): void;
}

/** Номер дня для подписи и ?day=: DayNumber, а без него — позиция в списке. */
export function dayNumberOf(days: OverviewDay[], d: OverviewDay): number {
  return d.day_number ?? days.indexOf(d) + 1;
}

/** Чтение ?day= (null — нет или мусор). */
export function readOverviewDayParam(): number | null {
  const n = Number(new URLSearchParams(window.location.search).get('day'));
  return Number.isInteger(n) && n > 0 ? n : null;
}

/** Запись ?day= без перезагрузки; null — убрать. */
export function writeOverviewDayParam(day: number | null) {
  const url = new URL(window.location.href);
  if (day == null) url.searchParams.delete('day'); else url.searchParams.set('day', String(day));
  window.history.replaceState(null, '', url.toString());
}

function shortDate(date: string): string {
  const [d, m] = date.split('/');
  return d && m ? `${d}/${m}` : date;
}

export default function OverviewDaySwitch({ days, value, onChange }: Props) {
  if (days.length < 2) return null;

  const chip = (key: string, label: React.ReactNode, active: boolean, onClick: () => void, title?: string) => (
    <button
      key={key}
      type="button"
      role="tab"
      aria-selected={active}
      title={title}
      onClick={onClick}
      className="rounded-full px-3 py-1 text-[12.5px] font-bold transition-colors"
      style={active
        ? { background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }
        : { background: 'var(--theme-mode-surface)', color: 'var(--theme-mode-text-secondary)',
            boxShadow: 'inset 0 0 0 1px var(--theme-mode-border)' }}
    >
      {label}
    </button>
  );

  return (
    <div role="tablist" aria-label="Show by day" className="mt-4 flex flex-wrap items-center gap-1.5">
      {chip('all', 'All days', value == null, () => onChange(null))}
      {days.map((d) => {
        const n = dayNumberOf(days, d);
        return chip(String(d.competition_id), <>Day {n} <span className="font-semibold opacity-75">· {shortDate(d.date)}</span></>,
          value === n, () => onChange(n), d.sub_name ?? undefined);
      })}
    </div>
  );
}
