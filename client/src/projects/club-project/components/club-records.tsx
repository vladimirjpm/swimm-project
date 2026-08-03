import React, { useState } from 'react';
import { useClubSeasonBest, type ClubSeasonBestGroup } from '../../../hooks/useClubSeasonBest';
import { routes } from '../../../utils/routes';
import { ClubRecordCard, type PoolFilter } from './club-record-card';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';

/**
 * Season best — заплывы пловцов клуба, которые в этом сезоне ЛУЧШИЕ ПО СТРАНЕ в своём слоте
 * (дисциплина × бассейн × пол × возрастная ступень). Лидер считается по всей базе, а не
 * внутри клуба: карточка отвечает «наши первые в Израиле», а не «наше лучшее».
 *
 * Ступени — как в таблицах федерации: до 18 лет каждый год отдельно, «adults» 19–24 одной
 * ступенью, мастерс пятилетками. Без ступеней слот забирал бы самый быстрый пловец страны
 * любого возраста, и дети не попадали бы в карточку никогда.
 *
 * ⚠ Два обязательства перед читателем, которые нельзя потерять:
 *  • карточка про ОДИН сезон и глобальный фильтр сезона страницы не слушает
 *    (решение Влада 2026-08-01);
 *  • «первый в Израиле» = первый среди ИМПОРТИРОВАННОГО, поэтому под заголовком стоит
 *    число стартов, вошедших в расчёт. Не убирать: юниорских и взрослых чемпионатов в
 *    базе может не быть вовсе, и лидерство по этим ступеням будет считаться по остаткам.
 *
 * Официальные рекорды (нац./возрастные/мастерс/мировые) — соседняя карточка
 * `club-record-wall.tsx`; общая у них форма (`club-record-card.tsx`), но не содержимое.
 */

interface Props {
  clubId: number;
}

/** Заголовок секции: «50m breaststroke · 25M · ♀». */
function groupTitle(g: ClubSeasonBestGroup): string {
  // Style.Name из БД сырой (individual_medley) — только косметика показа.
  const style = g.style_name.replace(/_/g, ' ');
  const pool = g.pool_type ? ` · ${g.pool_type.toUpperCase()}` : '';
  return `${g.distance}m ${style}${pool}`;
}

function ClubRecords({ clubId }: Props) {
  const [pool, setPool] = useState<PoolFilter>('all');
  const { groups, seasonLabel, total, meets, loading } = useClubSeasonBest(
    clubId,
    pool === 'all' ? null : pool,
  );

  return (
    <ClubRecordCard
      title={seasonLabel ? `Season ${seasonLabel} best` : 'Season best'}
      subtitle={`Club swimmers ranked #1 in Israel this season · among ${meets} meets in our database`}
      count={total}
      countLabel="#1 IN IL"
      pool={pool}
      onPool={setPool}
      isEmpty={groups.length === 0 && !loading}
      emptyText="No national-best times this season"
      // Секции сами задают внутреннюю сетку, поэтому внешняя не нужна.
      plainBody
    >
      <div className="flex flex-col gap-4">
        {groups.map((g) => (
          <div key={`${g.style_name}-${g.distance}-${g.pool_type}-${g.gender}`}>
            <div className="flex items-baseline gap-2">
              <span
                className="text-[12px] font-extrabold"
                style={{ color: 'var(--deep-text)' }}
              >
                {groupTitle(g)}
              </span>
              <span
                className="text-[12px]"
                style={{ color: g.gender === 'female' ? 'var(--deep-female)' : 'var(--deep-male)' }}
              >
                {g.gender === 'female' ? '♀' : '♂'}
              </span>
            </div>

            {/* Ряд возрастных ступеней: по возрастанию возраста, «n/a» в конце. */}
            <div className="mt-1.5 grid grid-cols-2 gap-2 sm:grid-cols-3">
              {g.items.map((it) => (
                <a
                  key={it.age_key}
                  href={routes.swimmer(it.swimmer_id)}
                  className={`deep-record-tile block no-underline ${
                    g.gender === 'female' ? 'deep-record-tile--f' : 'deep-record-tile--m'
                  }`}
                >
                  <div className="flex items-center justify-between gap-1">
                    <span
                      className="truncate text-[10.5px] font-black uppercase tracking-wide"
                      style={{ color: 'var(--deep-accent)' }}
                    >
                      {it.age_label}
                    </span>
                    {/* Плитка попадает сюда, только если это первое время страны в слоте. */}
                    <span
                      className="shrink-0 text-[10px] font-black"
                      style={{ color: 'var(--deep-gold)' }}
                      title="Best time in Israel this season (among imported meets)"
                    >
                      #1 IL
                    </span>
                  </div>
                  <div
                    className="mt-1 text-[19px] leading-none tabular-nums"
                    style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
                  >
                    <UI_SwimTime
                      time={it.time_original}
                      quality={it.suspect_reason ? { kind: 'protocol', reason: it.suspect_reason } : null}
                    />
                  </div>
                  <div
                    className="mt-1.5 truncate text-[11.5px] font-bold"
                    style={{ color: 'var(--deep-text)' }}
                    title={it.swimmer_name}
                  >
                    {it.swimmer_name}
                  </div>
                </a>
              ))}
            </div>
          </div>
        ))}
      </div>
    </ClubRecordCard>
  );
}

export default ClubRecords;
