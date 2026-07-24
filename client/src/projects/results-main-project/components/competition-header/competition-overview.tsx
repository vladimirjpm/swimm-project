import React from 'react';
import type { CompetitionOverview, OverviewClub, OverviewHighPoint } from './types';

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

function ClubsTable({ clubs, onOpenClub }: { clubs: OverviewClub[]; onOpenClub?: (club: string) => void }) {
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
              {onOpenClub ? (
                <button type="button" onClick={() => onOpenClub(c.club)} className="cursor-pointer bg-transparent p-0 font-bold hover:underline" style={{ color: 'inherit' }}>
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

// High Point Award: лучший по сумме очков в каждом возрасте, раздельно ♂/♀
// (design_handoff §High Point Award). Возраст-чип · имя (→ страница пловца) · клуб
// (→ таб Clubs) · очки; при ничье — бейдж «tie».
function HighPointColumn({
  awards, gender, onOpenClub,
}: { awards: OverviewHighPoint[]; gender: 'male' | 'female'; onOpenClub?: (club: string) => void }) {
  const isMale = gender === 'male';
  const circle: React.CSSProperties = isMale
    ? { background: 'rgba(29,78,216,.12)', color: '#1d4ed8' }
    : { background: 'rgba(190,24,93,.12)', color: '#be185d' };
  return (
    <div className="flex flex-col gap-0.5">
      <div className="flex items-center gap-1.5 pb-1">
        <span className="flex h-5 w-5 items-center justify-center rounded-full text-[12px] font-extrabold" style={circle}>
          {isMale ? '♂' : '♀'}
        </span>
        <span className="text-[11px] font-extrabold uppercase tracking-[0.05em]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {isMale ? 'Men' : 'Women'}
        </span>
      </div>
      {awards.map((a, i) => (
        <div
          key={`${a.age}-${a.swimmer_id}-${i}`}
          className="flex items-center gap-2.5 border-t py-1.5 text-[12.5px] font-bold first:border-t-0"
          style={{ borderColor: 'var(--theme-mode-border)' }}
        >
          <span
            className="w-[26px] shrink-0 rounded-[6px] py-0.5 text-center text-[11px] font-extrabold"
            style={{ background: 'color-mix(in srgb, var(--theme-primary) 12%, transparent)', color: 'var(--theme-primary)' }}
          >
            {a.age}
          </span>
          <a href={`./swimmer.html?swimmer=${a.swimmer_id}`} className="min-w-0 truncate hover:underline" dir="auto" style={{ color: 'var(--theme-mode-text)' }}>
            {a.first_name} {a.last_name}
            {a.is_tie && <span className="ml-1 text-[10px] font-extrabold" style={{ color: 'var(--theme-personal-accent)' }}>tie</span>}
          </a>
          <span className="min-w-0 flex-1 truncate font-semibold" dir="auto" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            {onOpenClub ? (
              <button type="button" onClick={() => onOpenClub(a.club)} className="cursor-pointer bg-transparent p-0 font-semibold hover:underline" style={{ color: 'inherit' }}>
                {a.club}
              </button>
            ) : a.club}
          </span>
          <span className="shrink-0 font-extrabold" style={{ color: 'var(--theme-primary)' }}>{a.points}</span>
        </div>
      ))}
    </div>
  );
}

function HighPointAward({ awards, onOpenClub }: { awards: OverviewHighPoint[]; onOpenClub?: (club: string) => void }) {
  if (awards.length === 0) return null;
  const men = awards.filter((a) => a.gender === 'male');
  const women = awards.filter((a) => a.gender === 'female');
  const ages = awards.map((a) => a.age);
  const range = ages.length ? `ages ${Math.min(...ages)}–${Math.max(...ages)}` : '';
  return (
    <div className="rounded-[12px] p-4" style={cardStyle}>
      <div className="mb-2 flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
        <span className="text-[14px] font-extrabold">🏆 High Point Award</span>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          best by points per age · {range} · ties keep all
        </span>
      </div>
      <div className="grid grid-cols-1 gap-x-3.5 gap-y-2 sm:grid-cols-2">
        {men.length > 0 && <HighPointColumn awards={men} gender="male" onOpenClub={onOpenClub} />}
        {women.length > 0 && <HighPointColumn awards={women} gender="female" onOpenClub={onOpenClub} />}
      </div>
    </div>
  );
}

interface Props {
  overview: CompetitionOverview | null;
  loading: boolean;
  /** Переход в другой таб (линки «Open … tab →», клик по клубу). */
  onOpenTab(tab: 'swims' | 'clubs' | 'media'): void;
  /** Диплинк на конкретный заплыв (best swim, строки рекордов). */
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
  /** Drill-down клуба: таб Clubs с выбранным клубом. */
  onOpenClub?(club: string): void;
}

export default function CompetitionOverviewContent({ overview, loading, onOpenTab, onOpenSwim, onOpenClub }: Props) {
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
            <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1">
              <button type="button"
                onClick={() => (onOpenSwim ? onOpenSwim(best) : onOpenTab('swims'))}
                className="bg-transparent p-0 text-[12px] font-bold hover:underline"
                style={{ color: 'var(--theme-primary)' }}>
                Open this swim →
              </button>
              {/* Диплинк на страницу пловца — только для личных заплывов (у эстафеты swimmer_id = «первый» ногой). */}
              {!best.is_relay && best.swimmer_id > 0 && (
                <a href={`./swimmer.html?swimmer=${best.swimmer_id}`}
                  className="text-[12px] font-bold hover:underline"
                  style={{ color: 'var(--theme-primary)' }}>
                  View swimmer →
                </a>
              )}
            </div>
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
            {medalist.swimmer_id > 0 && (
              <a href={`./swimmer.html?swimmer=${medalist.swimmer_id}`}
                className="mt-2 inline-block text-[12px] font-bold hover:underline"
                style={{ color: 'var(--theme-primary)' }}>
                View profile →
              </a>
            )}
          </div>
        )}

        {overview.records.length > 0 && (
          <div className="rounded-[12px] p-4" style={cardStyle}>
            <SectionTitle>New records</SectionTitle>
            {overview.records.map((r, i) => (
              <div
                key={i}
                className={`border-t py-1.5 text-[12.5px] font-bold first:border-t-0 ${
                  onOpenSwim && r.result_id != null ? 'cursor-pointer hover:underline' : ''
                }`}
                style={{ borderColor: 'var(--theme-mode-border)' }}
                dir="auto"
                onClick={onOpenSwim && r.result_id != null ? () => onOpenSwim(r) : undefined}
              >
                {r.distance}m {r.style_name} — {r.time} · {r.holder_name}
                {r.day_number != null && <span style={{ color: 'var(--theme-mode-text-muted)' }}> · Day {r.day_number}</span>}
              </div>
            ))}
          </div>
        )}

        {/* High Point Award (после New records, левая колонка). */}
        <HighPointAward awards={overview.high_point_awards} onOpenClub={onOpenClub} />
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
            <ClubsTable clubs={overview.top_clubs} onOpenClub={onOpenClub} />
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
