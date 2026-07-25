import React from 'react';
import type { OverviewSummary } from './types';

// Модуль «Summary» таба Overview: сводка соревнования (результаты/дни/пловцы/клубы).
// Вынесен отдельным файлом для наглядности структуры overview; в DOM помечен
// data-module="summary" — чтобы блок было легко найти в инспекторе.

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  color: 'var(--theme-mode-text)',
  boxShadow: 'var(--theme-mode-card-shadow)',
  border: '1px solid var(--theme-mode-card-border)',
};

export default function CompetitionSummary({ summary }: { summary: OverviewSummary }) {
  const rows = [
    ['Results so far', summary.result_count],
    ['Days', summary.day_count],
    ['Swimmers', summary.swimmer_count],
    ['Clubs', summary.club_count],
  ] as const;

  return (
    <div data-module="summary" className="rounded-[12px] p-4" style={cardStyle}>
      <div className="mb-2 text-[14px] font-extrabold">Summary</div>
      {rows.map(([label, value]) => (
        <div
          key={label}
          className="flex justify-between border-t py-1.5 text-[12.5px] first:border-t-0"
          style={{ borderColor: 'var(--theme-mode-border)' }}
        >
          <span className="font-semibold" style={{ color: 'var(--theme-mode-text-secondary)' }}>{label}</span>
          <span className="font-extrabold">{value}</span>
        </div>
      ))}
    </div>
  );
}
