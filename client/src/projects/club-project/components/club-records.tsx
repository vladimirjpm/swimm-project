import React, { useState } from 'react';
import { useClubRecords } from '../../../hooks/useClubRecords';
import { routes } from '../../../utils/routes';

/**
 * Record wall — лучшие времена клуба по оси стиль×дистанция×бассейн×пол
 * (CARDS.md §5, club-page-cards-sonnet.md §4.5). ЕДИНСТВЕННАЯ карточка с локальным фильтром
 * (25м и 50м физически несравнимы — README «Модель страницы»); остальные карточки читают
 * только глобальный скоуп.
 */

interface Props {
  clubId: number;
}

type PoolFilter = 'all' | '25m' | '50m';

function ClubRecords({ clubId }: Props) {
  const [pool, setPool] = useState<PoolFilter>('all');
  const { data, loading } = useClubRecords(clubId, pool === 'all' ? null : pool);

  return (
    <section className="deep-card mb-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="deep-card-title">Record wall</div>
          <div className="deep-card-sub mt-1">Best times by discipline</div>
        </div>

        <div className="flex items-center gap-3">
          <span className="deep-count-badge">
            <b>{data.length}</b> RECORDS
          </span>
          <div className="deep-seg">
            {(['all', '25m', '50m'] as PoolFilter[]).map((p) => (
              <button
                key={p}
                type="button"
                className={pool === p ? 'active' : ''}
                onClick={() => setPool(p)}
              >
                {p === 'all' ? 'All' : p}
              </button>
            ))}
          </div>
        </div>
      </div>

      {data.length === 0 && !loading ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No records yet
        </div>
      ) : (
        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
          {data.map((r) => {
            const isFemale = r.gender === 'female';
            // При «All» к дисциплине добавляется суффикс бассейна — иначе 25м и 50м тайлы
            // одной дисциплины неотличимы (CARDS.md §5).
            const poolSuffix = pool === 'all' && r.pool_type ? ` · ${r.pool_type.toUpperCase()}` : '';
            const year = r.date ? r.date.slice(-4) : '';
            return (
              <a
                key={`${r.style_name}-${r.distance}-${r.pool_type}-${r.gender}`}
                href={routes.swimmer(r.swimmer_id)}
                className={`deep-record-tile block no-underline ${
                  isFemale ? 'deep-record-tile--f' : 'deep-record-tile--m'
                }`}
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div
                      className="truncate text-[11px] font-extrabold"
                      style={{ color: 'var(--deep-text-mute)' }}
                    >
                      {/* Style.Name из БД сырой (individual_medley) — только косметика показа. */}
                      {r.distance}m {r.style_name.replace(/_/g, ' ')}
                      {poolSuffix}
                    </div>
                    <div
                      className="mt-1 text-[22px] leading-none tabular-nums"
                      style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
                    >
                      {r.time_original}
                    </div>
                  </div>
                  <span
                    className="shrink-0 text-[13px]"
                    style={{ color: isFemale ? 'var(--deep-female)' : 'var(--deep-male)' }}
                  >
                    {isFemale ? '♀' : '♂'}
                  </span>
                </div>
                <div className="mt-2 truncate text-[12px] font-bold" style={{ color: 'var(--deep-text)' }}>
                  {r.swimmer_name}
                </div>
                <div className="text-[10.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
                  {year}
                </div>
              </a>
            );
          })}
        </div>
      )}
    </section>
  );
}

export default ClubRecords;
