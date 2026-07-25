import React from 'react';
import type { OverviewClub } from './types';

// Общие для модулей таба Overview стили/хелперы (карточка, заголовок секции,
// таблица клубов, гендерная шапка). Держим тут, чтобы каждый блок был отдельным
// компонентом-файлом и переиспользовал единый вид.

export const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  color: 'var(--theme-mode-text)',
  boxShadow: 'var(--theme-mode-card-shadow)',
  border: '1px solid var(--theme-mode-card-border)',
};

export function SectionTitle({ children }: { children: React.ReactNode }) {
  return <div className="mb-2 text-[14px] font-extrabold">{children}</div>;
}

export function ClubsTable({ clubs, onOpenClub, showMedals = true }: {
  clubs: OverviewClub[]; onOpenClub?: (club: string) => void; showMedals?: boolean;
}) {
  return (
    <table className="w-full border-collapse text-[12.5px]">
      <thead>
        <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
          <th className="py-1 pr-2">#</th>
          <th className="py-1 pr-2">Club</th>
          {showMedals && <th className="py-1 pr-2">Medals</th>}
          <th className="py-1 text-right">Rating</th>
        </tr>
      </thead>
      <tbody>
        {clubs.map((c, i) => (
          <tr key={c.club} className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
            <td className="py-1.5 pr-2 font-bold" style={{ color: 'var(--theme-mode-text-muted)' }}>{i + 1}</td>
            <td className="py-1.5 pr-2 font-bold" dir="auto">
              {onOpenClub ? (
                <button type="button" onClick={() => onOpenClub(c.club)} className="cursor-pointer bg-transparent p-0 font-bold hover:underline" style={{ color: 'inherit' }}>
                  {c.club}
                </button>
              ) : c.club}
            </td>
            {showMedals && <td className="py-1.5 pr-2 whitespace-nowrap">🥇{c.gold} 🥈{c.silver} 🥉{c.bronze}</td>}
            <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>{c.points}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

// Best swim / Most decorated · вариант 4 (♂ / ♀). Гендерные цвета — по образцу row-male/female.
export const GENDER_CIRCLE: Record<'male' | 'female', React.CSSProperties> = {
  male: { background: 'rgba(29,78,216,.12)', color: '#1d4ed8' },
  female: { background: 'rgba(190,24,93,.12)', color: '#be185d' },
};
export const halfCardStyle: React.CSSProperties = { border: '1px solid var(--theme-mode-border-input, #d6e0da)' };
export const halfCardClass = 'flex min-w-0 flex-1 flex-col rounded-[10px] p-3 no-underline';

export function GenderHeader({ gender }: { gender: 'male' | 'female' }) {
  return (
    <div className="flex items-center gap-[7px]">
      <span className="flex h-[22px] w-[22px] items-center justify-center rounded-full text-[12px] font-extrabold" style={GENDER_CIRCLE[gender]}>
        {gender === 'male' ? '♂' : '♀'}
      </span>
      <span className="text-[11px] font-extrabold uppercase tracking-[0.05em]" style={{ color: 'var(--theme-mode-text-muted)' }}>
        {gender === 'male' ? 'Men' : 'Women'}
      </span>
    </div>
  );
}
