import React from 'react';
import type { ClubGroupTile } from '../../../hooks/useClubOverview';
import { groupAgeRange } from './club-groups';

/**
 * Фильтр зачётных групп страницы клуба.
 *
 * Сезон отсюда УЕХАЛ в кольцевую карусель над табами (club-season-carousel.tsx,
 * handoff filter-season 4c) — здесь остались только плитки групп, и те временно
 * скрыты флагом ниже. Карточки по-прежнему своих фильтров не заводят: они читают
 * общий скоуп (исключение — физический бассейн у стены рекордов).
 */

interface Props {
  groups: ClubGroupTile[];
  group: string | null;
  onGroup: (group: string | null) => void;
}

/**
 * Ряд плиток зачётных групп ВРЕМЕННО СКРЫТ (handoff club_tabs: «не рендерить, но не
 * удалять»). Разметка и логика фильтра целы и включаются одним флагом — вернём по
 * решению. Скоуп при этом остаётся: group=null = все группы.
 */
export const SHOW_GROUP_TILES = false;

function ClubFilters({ groups, group, onGroup }: Props) {
  return (
    <section className="deep-card mb-4">
      <div className="flex flex-col gap-4">
        {SHOW_GROUP_TILES && groups.length > 0 && (
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
          className="flex h-8 w-8 items-center justify-center rounded-full text-[14px] font-black"
          style={{ background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }}
        >
          {tile.badge ?? tile.name.slice(0, 1)}
        </span>
        <span className="text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
          {tile.name}
        </span>
        {groupAgeRange(tile.name) && (
          <span className="text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            {groupAgeRange(tile.name)}
          </span>
        )}
      </div>
      {/* Ряд рангов выровнен по тексту, а не по медальону: отступ = ширина медальона (32) + gap (8). */}
      <div className="mt-2 ml-10 flex gap-3 text-[11px] font-bold">
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
