import React from 'react';
import type { CompetitionOverview, OverviewClub } from './types';

// Контент таба Overview, v1-каркас (grid 12: слева span-8, справа span-4; <lg — одна
// колонка). Композиция — по design_handoff_competition_overview/README (вариант 1b);
// пиксельная доводка и блоки Media/Records/персональный — отдельными шагами.
// Суперлативы вычислимы из результатов, поэтому пустого дэшборда не бывает.

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  color: 'var(--theme-mode-text)',
  boxShadow: 'var(--theme-mode-card-shadow)',
  border: '1px solid var(--theme-mode-card-border)',
};

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <div className="mb-2 text-[14px] font-extrabold">{children}</div>;
}

function ClubsTable({ clubs, onOpenClubs }: { clubs: OverviewClub[]; onOpenClubs?: () => void }) {
  return (
    <table className="w-full border-collapse text-[12.5px]">
      <thead>
        <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
          <th className="py-1 pr-2">#</th>
          <th className="py-1 pr-2">Club</th>
          <th className="py-1 pr-2">Medals</th>
          <th className="py-1 text-right">Rating</th>
        </tr>
      </thead>
      <tbody>
        {clubs.map((c, i) => (
          <tr key={c.club} className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
            <td className="py-1.5 pr-2 font-bold" style={{ color: 'var(--theme-mode-text-muted)' }}>{i + 1}</td>
            <td className="py-1.5 pr-2 font-bold" dir="auto">
              {onOpenClubs ? (
                <button type="button" onClick={onOpenClubs} className="bg-transparent p-0 font-bold hover:underline" style={{ color: 'inherit' }}>
                  {c.club}
                </button>
              ) : c.club}
            </td>
            <td className="py-1.5 pr-2 whitespace-nowrap">🥇{c.gold} 🥈{c.silver} 🥉{c.bronze}</td>
            <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>{c.points}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

interface Props {
  overview: CompetitionOverview | null;
  loading: boolean;
  /** Переход в другой таб (линки «Open … tab →», клик по клубу). */
  onOpenTab(tab: 'swims' | 'clubs' | 'media'): void;
}

export default function CompetitionOverviewContent({ overview, loading, onOpenTab }: Props) {
  if (!overview) {
    return (
      <div className="mt-4 flex min-h-[140px] items-center justify-center rounded-[14px] text-[13px] font-semibold"
        style={{ ...cardStyle, color: 'var(--theme-mode-text-muted)' }}>
        {loading ? 'Loading overview…' : 'No overview data for this competition.'}
      </div>
    );
  }

  const best = overview.best_swim;
  const medalist = overview.top_medalist;
  const s = overview.summary;

  return (
    <div className="mt-4 grid grid-cols-1 gap-3 lg:grid-cols-12">
      {/* Левая колонка */}
      <div className="flex flex-col gap-3 lg:col-span-8">
        {best && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>Best swim of the competition</SectionTitle>
            <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="text-[28px] font-black leading-none md:text-[38px]">{best.time}</span>
              <span className="text-[15px] font-extrabold" style={{ color: 'var(--theme-primary)' }}>
                {best.international_points} pts
              </span>
            </div>
            <div className="mt-1.5 text-[12.5px] font-bold" dir="auto">
              {best.distance}m {best.style_name} ·{' '}
              {best.is_relay && best.relay_team_name
                ? best.relay_team_name
                : `${best.first_name} ${best.last_name}`}
              <span style={{ color: 'var(--theme-mode-text-muted)' }}> · {best.club}</span>
              {best.day_number != null && (
                <span style={{ color: 'var(--theme-mode-text-muted)' }}> · Day {best.day_number}</span>
              )}
            </div>
            <button type="button" onClick={() => onOpenTab('swims')}
              className="mt-2 bg-transparent p-0 text-[12px] font-bold hover:underline"
              style={{ color: 'var(--theme-primary)' }}>
              Open Swims tab →
            </button>
          </div>
        )}

        {medalist && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>Most decorated swimmer</SectionTitle>
            <div className="text-[15px] font-extrabold" dir="auto">
              {medalist.first_name} {medalist.last_name}
              <span className="text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}> · {medalist.club}</span>
            </div>
            <div className="mt-1 text-[13px] font-bold">🥇 {medalist.gold} 🥈 {medalist.silver} 🥉 {medalist.bronze}</div>
          </div>
        )}

        {overview.records.length > 0 && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>New records</SectionTitle>
            {overview.records.map((r, i) => (
              <div key={i} className="border-t py-1.5 text-[12.5px] font-bold first:border-t-0" style={{ borderColor: 'var(--theme-mode-border)' }} dir="auto">
                {r.distance}m {r.style_name} — {r.time} · {r.holder_name}
                {r.day_number != null && <span style={{ color: 'var(--theme-mode-text-muted)' }}> · Day {r.day_number}</span>}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Правая колонка */}
      <div className="flex flex-col gap-3 lg:col-span-4">
        <div className="rounded-[12px] p-4" style={cardStyle}>
          <SectionTitle>Summary</SectionTitle>
          {([
            ['Results so far', s.result_count],
            ['Days', s.day_count],
            ['Swimmers', s.swimmer_count],
            ['Clubs', s.club_count],
          ] as const).map(([label, value]) => (
            <div key={label} className="flex justify-between border-t py-1.5 text-[12.5px] first:border-t-0" style={{ borderColor: 'var(--theme-mode-border)' }}>
              <span className="font-semibold" style={{ color: 'var(--theme-mode-text-secondary)' }}>{label}</span>
              <span className="font-extrabold">{value}</span>
            </div>
          ))}
        </div>

        {overview.top_clubs.length > 0 && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>Top clubs</SectionTitle>
            <ClubsTable clubs={overview.top_clubs} onOpenClubs={() => onOpenTab('clubs')} />
            <button type="button" onClick={() => onOpenTab('clubs')}
              className="mt-2 bg-transparent p-0 text-[12px] font-bold hover:underline"
              style={{ color: 'var(--theme-primary)' }}>
              Clubs tab →
            </button>
          </div>
        )}

        {overview.top_clubs_men.length > 0 && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>Top clubs · Men ♂</SectionTitle>
            <ClubsTable clubs={overview.top_clubs_men} />
          </div>
        )}
        {overview.top_clubs_women.length > 0 && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>Top clubs · Women ♀</SectionTitle>
            <ClubsTable clubs={overview.top_clubs_women} />
          </div>
        )}
      </div>
    </div>
  );
}
