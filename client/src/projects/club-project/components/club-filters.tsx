import React from 'react';
import type { ClubGroupTile, ClubSeasonOption } from '../../../hooks/useClubOverview';

/**
 * Глобальные фильтры страницы клуба: сезон и зачётная группа.
 * Это ЕДИНСТВЕННЫЙ блок фильтров на всю страницу — карточки читают выбранный скоуп
 * и своих фильтров не заводят (исключение — физический параметр вроде бассейна
 * у стены рекордов). См. design_handoff_club_page/README.md.
 */

interface Props {
  seasons: ClubSeasonOption[];
  groups: ClubGroupTile[];
  season: number | null;
  group: string | null;
  onSeason: (season: number | null) => void;
  onGroup: (group: string | null) => void;
}

function ClubFilters({ seasons, groups, season, group, onSeason, onGroup }: Props) {
  return (
    <section className="deep-card mb-4">
      <div className="flex flex-col gap-4">
        <div>
          <div className="deep-card-sub mb-2 uppercase tracking-wide">Season</div>
          {/* Сезонов 20+ и их число растёт — ряд горизонтально скроллится, All закреплён. */}
          <div className="flex items-center gap-2">
            <button
              type="button"
              className={`deep-pill ${season === null ? 'deep-pill--active' : ''}`}
              onClick={() => onSeason(null)}
            >
              All
            </button>
            <div className="deep-swipe-clip">
              <div className="deep-swipe-row">
                {seasons.map((s) => (
                  <button
                    key={s.season}
                    type="button"
                    className={`deep-pill ${season === s.season ? 'deep-pill--active' : ''}`}
                    onClick={() => onSeason(s.season)}
                  >
                    {s.label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>

        {groups.length > 0 && (
          <div>
            <div className="deep-card-sub mb-2 uppercase tracking-wide">Group</div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                className={`deep-pill ${group === null ? 'deep-pill--active' : ''}`}
                onClick={() => onGroup(null)}
              >
                All
              </button>
              <div className="deep-swipe-clip">
                <div className="deep-swipe-row">
                  {groups.map((g) => (
                    <GroupTile
                      key={g.key}
                      tile={g}
                      active={group === g.key}
                      // Повторный клик по активной плитке снимает фильтр (макет).
                      onClick={() => onGroup(group === g.key ? null : g.key)}
                    />
                  ))}
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </section>
  );
}

function GroupTile({
  tile,
  active,
  onClick,
}: {
  tile: ClubGroupTile;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`deep-group-tile ${active ? 'deep-group-tile--active' : ''}`}
    >
      <div className="flex items-center gap-2">
        <span
          className="flex h-6 w-6 items-center justify-center rounded-full text-[11px] font-black"
          style={{ background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }}
        >
          {tile.badge ?? tile.name.slice(0, 1)}
        </span>
        <span className="text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
          {tile.name}
        </span>
      </div>
      <div className="mt-2 flex gap-3 text-[11px] font-bold">
        <RankHint icon="❄" rank={tile.winter_rank} />
        <RankHint icon="☀" rank={tile.summer_rank} />
        <RankHint icon="🌊" rank={tile.open_water_rank} />
      </div>
    </button>
  );
}

/** Ранг подписывается всегда: сравнивать между соревнованиями можно только его, не очки. */
function RankHint({ icon, rank }: { icon: string; rank: number | null }) {
  if (rank == null) return null;
  return (
    <span style={{ color: rank === 1 ? 'var(--deep-gold)' : 'var(--deep-text-mute)' }}>
      {icon} #{rank}
    </span>
  );
}

export default ClubFilters;
