import React from 'react';
import { useClubSummary } from '../../../../hooks/useClubSummary';

// Таб Clubs, v1: полный клубный зачёт источника с сервера (/api/club-summary, фаза 3.4).
// Drill-down по клубу (?clubId= → пловцы/результаты клуба) — следующий шаг НАВ-контракта.

export default function CompetitionClubs({ sourceParams }: { sourceParams?: Record<string, string> }) {
  const clubs = useClubSummary(sourceParams, !!sourceParams);

  return (
    <div
      className="mt-4 overflow-x-auto rounded-[12px] p-4"
      style={{
        background: 'var(--theme-mode-surface)',
        color: 'var(--theme-mode-text)',
        boxShadow: 'var(--theme-mode-card-shadow)',
        border: '1px solid var(--theme-mode-card-border)',
      }}
    >
      <div className="mb-2 text-[14px] font-extrabold">Club standings</div>
      {clubs.length === 0 ? (
        <div className="py-6 text-center text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No club data for this competition.
        </div>
      ) : (
        <table className="w-full border-collapse text-[12.5px]">
          <thead>
            <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
              <th className="py-1 pr-2">#</th>
              <th className="py-1 pr-2">Club</th>
              <th className="py-1 pr-2">Swimmers</th>
              <th className="py-1 pr-2">Medals</th>
              <th className="py-1 text-right">Rating</th>
            </tr>
          </thead>
          <tbody>
            {clubs.map((c, i) => (
              <tr key={c.club} className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
                <td className="py-1.5 pr-2 font-bold" style={{ color: 'var(--theme-mode-text-muted)' }}>{i + 1}</td>
                <td className="py-1.5 pr-2 font-bold" dir="auto">{c.club}</td>
                <td className="py-1.5 pr-2">{c.swimmerCount}</td>
                <td className="py-1.5 pr-2 whitespace-nowrap">🥇{c.gold} 🥈{c.silver} 🥉{c.bronze}</td>
                <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>{c.points}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
