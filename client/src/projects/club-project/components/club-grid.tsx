import React from 'react';
import type { ClubGridCell, ClubGridYear } from '../../../hooks/useClubOverview';
import { groupAgeRange } from './club-groups';

/**
 * ⭐ Season × Group grid — главная карточка страницы клуба.
 *
 * Год = секция, внутри строка на каждую зачётную группу, а в строке — линии чемпионатов:
 * `[ранг-сегменты] ❄ #3` и `[сегменты] ☀ #2`. Сегментов тем больше, чем выше место:
 * #10 и хуже — 1, каждое место выше +1, #1 — 10 (см. CARDS.md §3).
 *
 * ⚠ Показываются РАНГИ, а не очки: очки считаются по правилу конкретного соревнования
 * и между стартами несравнимы (docs/plans/club-page-model.md §5).
 *
 * Пустая клетка — штатный случай: чемпионат могли отменить.
 */

const MAX_SEGMENTS = 10;

interface Props {
  grid: ClubGridYear[];
  currentSeason: number | null;
  /** Клик по линии зачёта раскрывает его таблицей (карточка Standings). */
  onPickStanding: (competitionId: number) => void;
  selectedCompetitionId: number | null;
}

function ClubGrid({ grid, currentSeason, onPickStanding, selectedCompetitionId }: Props) {
  if (grid.length === 0) {
    return (
      <section className="deep-card h-full">
        <div className="deep-card-title">Season × Group</div>
        <div className="mt-3 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No club standings yet
        </div>
      </section>
    );
  }

  // «Текущий» сезон подсвечивается ярко, прошлые приглушены (-dim).
  const brightest = currentSeason ?? Math.max(...grid.map((y) => y.season));

  return (
    <section className="deep-card h-full">
      <div className="deep-card-title">Season × Group</div>
      <div className="deep-card-sub mt-1">Rank in each championship · higher bar = better place</div>

      <div className="mt-4 flex flex-col gap-6">
        {grid.map((year) => (
          <div key={year.season} className="flex gap-3 sm:gap-5">
            <div
              className="w-[44px] shrink-0 text-[13px] leading-none sm:w-[74px] sm:text-[22px]"
              style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text-faint)' }}
            >
              {year.label}
            </div>

            <div className="flex min-w-0 flex-1 flex-col gap-3">
              {year.rows.length === 0 ? (
                <div className="text-[12px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
                  no results in {year.label}
                </div>
              ) : (
                year.rows.map((row) => (
                  // Медальон и подпись центрируются по всей высоте строки (линий зачётов
                  // может быть от одной до трёх), а не липнут к её верху.
                  <div key={row.group_key} className="flex items-center gap-3">
                    <span
                      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-[14px] font-black"
                      style={{ background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }}
                    >
                      {row.badge ?? row.group_name.slice(0, 1)}
                    </span>
                    {/* Ширина больше прежней (68/92): в подпись входит и возрастная лента. */}
                    <span
                      className="w-[86px] shrink-0 truncate text-[12px] font-extrabold sm:w-[112px]"
                      style={{ color: 'var(--deep-text-mute)' }}
                    >
                      {row.group_name}
                      {groupAgeRange(row.group_name) && (
                        <span className="ml-1.5" style={{ color: 'var(--deep-text-faint)' }}>
                          {groupAgeRange(row.group_name)}
                        </span>
                      )}
                    </span>

                    {/* Линии чемпионатов — ОДНА ПОД ДРУГОЙ (CARDS.md §3: «[сегменты][❄ #3] /
                        [сегменты][☀ #2]»), а не в ряд: так карточка живёт в половине ширины
                        и ранги ❄/☀ читаются столбиком. */}
                    <div className="flex min-w-0 flex-1 flex-col gap-1">
                    <StandingLine
                      icon="❄"
                      kind="winter"
                      cell={row.winter}
                      dim={year.season !== brightest}
                      selected={selectedCompetitionId === row.winter?.competition_id}
                      onPick={onPickStanding}
                    />
                    <StandingLine
                      icon="☀"
                      kind="summer"
                      cell={row.summer}
                      dim={year.season !== brightest}
                      selected={selectedCompetitionId === row.summer?.competition_id}
                      onPick={onPickStanding}
                    />
                    <StandingLine
                      icon="🌊"
                      kind="ow"
                      cell={row.open_water}
                      dim={year.season !== brightest}
                      selected={selectedCompetitionId === row.open_water?.competition_id}
                      onPick={onPickStanding}
                    />
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function StandingLine({
  icon,
  kind,
  cell,
  dim,
  selected,
  onPick,
}: {
  icon: string;
  kind: 'winter' | 'summer' | 'ow';
  cell: ClubGridCell | null;
  dim: boolean;
  selected: boolean;
  onPick: (competitionId: number) => void;
}) {
  if (!cell) return null;

  const segments = segmentsForRank(cell.rank);
  const cls = `deep-rank-seg deep-rank-seg--${kind}${dim ? '-dim' : ''}`;

  return (
    <button
      type="button"
      onClick={() => onPick(cell.competition_id)}
      className="flex w-full items-center gap-2 rounded-lg px-2 py-1"
      style={{
        background: selected ? 'var(--deep-accent-soft)' : 'transparent',
        border: `1px solid ${selected ? 'var(--deep-accent-border)' : 'transparent'}`,
      }}
      title={`${cell.points} points`}
    >
      <span className="flex items-end gap-[3px]">
        {Array.from({ length: segments }, (_, i) => (
          <i key={i} className={cls} />
        ))}
      </span>
      <span
        className="text-[12px] font-extrabold"
        style={{ color: cell.rank === 1 ? 'var(--deep-gold)' : 'var(--deep-text-mute)' }}
      >
        {icon} #{cell.rank}
      </span>
    </button>
  );
}

/** #1 → 10 сегментов, #10 и хуже → 1. Ровно как в CARDS.md §3. */
export function segmentsForRank(rank: number): number {
  if (rank <= 0) return 1;
  return Math.max(1, MAX_SEGMENTS + 1 - Math.min(rank, MAX_SEGMENTS));
}

export default ClubGrid;
