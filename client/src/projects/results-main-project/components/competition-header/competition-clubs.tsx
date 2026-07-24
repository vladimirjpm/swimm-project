import React, { useMemo, useState } from 'react';
import { useAppSelector } from '../../../../store/store';
import { useClubSummary } from '../../../../hooks/useClubSummary';

// Таб Clubs: клубный зачёт источника (/api/club-summary, фаза 3.4) + drill-down по клубу:
// клик по строке → карточка клуба (пловцы, их заплывы/медали из загруженных результатов)
// + «Open in Swims →». Выбор пишется в ?club=<имя> (НАВ-контракт задумывал clubId, но у
// зачёта нет стабильного id — ключ клуба текстовый, включая эстафетные команды).

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  color: 'var(--theme-mode-text)',
  boxShadow: 'var(--theme-mode-card-shadow)',
  border: '1px solid var(--theme-mode-card-border)',
};

interface Props {
  sourceParams?: Record<string, string>;
  /** Переход в Swims с фильтром по клубу. */
  onOpenSwimsForClub?(club: string): void;
}

function writeClubUrl(club: string | null) {
  const url = new URL(window.location.href);
  if (club) url.searchParams.set('club', club);
  else url.searchParams.delete('club');
  window.history.replaceState(null, '', url.toString());
}

export default function CompetitionClubs({ sourceParams, onOpenSwimsForClub }: Props) {
  const clubs = useClubSummary(sourceParams, !!sourceParams);
  const selectedSource = useAppSelector((s) => s.dataSourceSelected);
  const [selectedClub, setSelectedClub] = useState<string | null>(
    () => new URLSearchParams(window.location.search).get('club'),
  );

  const selectClub = (club: string | null) => {
    setSelectedClub(club);
    writeClubUrl(club);
  };

  // Пловцы выбранного клуба — из загруженных результатов источника (ключ клуба
  // в зачёте — club → relay_team_name → club_en, здесь матчим по res.club).
  const clubSwimmers = useMemo(() => {
    if (!selectedClub) return [];
    const rows = (selectedSource?.results ?? []).filter((r: any) => r.club === selectedClub);
    const bySwimmer = new Map<string, { name: string; swims: number; gold: number; silver: number; bronze: number; bestPts: number }>();
    for (const r of rows as any[]) {
      const name = `${r.first_name ?? ''} ${r.last_name ?? ''}`.trim() || r.relay_team_name || '—';
      const e = bySwimmer.get(name) ?? { name, swims: 0, gold: 0, silver: 0, bronze: 0, bestPts: 0 };
      e.swims += 1;
      const pos = Number(r.position);
      if (pos === 1) e.gold += 1;
      else if (pos === 2) e.silver += 1;
      else if (pos === 3) e.bronze += 1;
      e.bestPts = Math.max(e.bestPts, r.international_points ?? 0);
      bySwimmer.set(name, e);
    }
    return [...bySwimmer.values()].sort(
      (a, b) => b.gold - a.gold || b.silver - a.silver || b.bronze - a.bronze || b.bestPts - a.bestPts,
    );
  }, [selectedClub, selectedSource]);

  const selectedSummary = selectedClub ? clubs.find((c) => c.club === selectedClub) : null;

  return (
    <div className="mt-4 flex flex-col gap-3">
      {/* Карточка выбранного клуба (drill-down) */}
      {selectedClub && (
        <div className="rounded-[12px] p-4" style={cardStyle}>
          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
            <div className="text-[15px] font-extrabold" dir="auto">
              {selectedClub}
              {selectedSummary && (
                <span className="ml-2 text-[12.5px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
                  🥇{selectedSummary.gold} 🥈{selectedSummary.silver} 🥉{selectedSummary.bronze} · {selectedSummary.points} pts
                </span>
              )}
            </div>
            <span className="flex items-center gap-2">
              {onOpenSwimsForClub && (
                <button
                  type="button"
                  onClick={() => onOpenSwimsForClub(selectedClub)}
                  className="cursor-pointer bg-transparent p-0 text-[12px] font-extrabold hover:underline"
                  style={{ color: 'var(--theme-primary)' }}
                >
                  Open in Swims →
                </button>
              )}
              <button
                type="button"
                onClick={() => selectClub(null)}
                className="cursor-pointer bg-transparent p-0 text-[13px]"
                style={{ color: 'var(--theme-mode-text-muted)' }}
                aria-label="Close club details"
              >
                ✕
              </button>
            </span>
          </div>
          {clubSwimmers.length === 0 ? (
            <div className="py-3 text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              Swimmer details appear when results are loaded.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-[12.5px]">
                <thead>
                  <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
                    <th className="py-1 pr-2">Swimmer</th>
                    <th className="py-1 pr-2">Swims</th>
                    <th className="py-1 pr-2">Medals</th>
                    <th className="py-1 text-right">Best pts</th>
                  </tr>
                </thead>
                <tbody>
                  {clubSwimmers.map((s) => (
                    <tr key={s.name} className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
                      <td className="py-1.5 pr-2 font-bold" dir="auto">{s.name}</td>
                      <td className="py-1.5 pr-2">{s.swims}</td>
                      <td className="py-1.5 pr-2 whitespace-nowrap">
                        {s.gold > 0 && `🥇${s.gold} `}
                        {s.silver > 0 && `🥈${s.silver} `}
                        {s.bronze > 0 && `🥉${s.bronze}`}
                      </td>
                      <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>
                        {s.bestPts > 0 ? s.bestPts : ''}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Полный зачёт */}
      <div className="overflow-x-auto rounded-[12px] p-4" style={cardStyle}>
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
                <tr
                  key={c.club}
                  className={`cursor-pointer border-t ${selectedClub === c.club ? 'font-extrabold' : ''}`}
                  style={{
                    borderColor: 'var(--theme-mode-border)',
                    background: selectedClub === c.club
                      ? 'color-mix(in srgb, var(--theme-primary) 8%, transparent)'
                      : undefined,
                  }}
                  onClick={() => selectClub(selectedClub === c.club ? null : c.club)}
                >
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
    </div>
  );
}
