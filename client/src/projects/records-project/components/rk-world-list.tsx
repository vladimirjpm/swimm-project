import React, { useMemo } from 'react';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import type { RegionRecord } from '../../../hooks/useRegionRecords';
import { routes } from '../../../utils/routes';
import {
  RK_STROKES, distanceLabel, holderLabel, isRelay, strokeLabel, type RkFilters,
} from '../rk-disciplines';

/**
 * Таб «World records»: все мировые рекорды одного бассейна и пола списком.
 *
 * Дисциплины идут в порядке программы (`RK_STROKES`: стиль, в нём личные дистанции, потом
 * эстафеты), а не по алфавиту и не по порядку ответа API: «100m» строкой раньше «50m», и
 * список читался бы как перемешанный.
 *
 * Название дисциплины — ссылка на рейтинг стран по ней: мировой рекорд здесь отвечает на
 * «сколько», а на «кто ближе всех» отвечает соседний таб. Держим связь между табами, а не
 * заставляем выбирать дисциплину второй раз.
 */

interface Props {
  records: RegionRecord[];
  filters: RkFilters;
}

/** Ключ дисциплины для порядка программы: индекс стиля, затем индекс дистанции в нём. */
function programmeOrder(style: string, distance: string): number {
  const si = RK_STROKES.findIndex((s) => s.key === style);
  if (si < 0) return Number.MAX_SAFE_INTEGER;
  const stroke = RK_STROKES[si];
  const all = [...stroke.distances, ...stroke.relays];
  const di = all.indexOf(distance);
  return si * 100 + (di < 0 ? 99 : di);
}

const RkWorldList: React.FC<Props> = ({ records, filters }) => {
  const rows = useMemo(
    () => records
      .filter((r) => r.pool_type === filters.poolType && r.gender === filters.gender)
      .sort((a, b) => programmeOrder(a.style, a.distance) - programmeOrder(b.style, b.distance)),
    [records, filters.poolType, filters.gender],
  );

  if (rows.length === 0) {
    return <div className="rk-state">No world records for this pool and gender.</div>;
  }

  return (
    <div className="rk-table" role="table" aria-label="World records">
      <div className="rk-row rk-row--wr rk-row--head" role="row">
        <span role="columnheader">Event</span>
        <span role="columnheader">Time</span>
        <span role="columnheader">Holder</span>
        <span role="columnheader">Nation</span>
        <span role="columnheader">Date</span>
      </div>

      {rows.map((r) => {
        const quality = r.issue_reason ? { kind: 'record' as const, reason: r.issue_reason } : null;
        const flagged = swimFlaggedRowProps(quality);
        const relay = isRelay(r.distance);

        return (
          <div
            key={`${r.style}|${r.distance}`}
            role="row"
            className={['rk-row', 'rk-row--wr', flagged.className ?? ''].filter(Boolean).join(' ')}
            title={flagged.title}
          >
            <span className="rk-cell rk-cell--event" role="cell">
              <a
                className="rc-link"
                href={routes.records({
                  stroke: r.style, distance: r.distance,
                  gender: filters.gender, poolType: filters.poolType,
                })}
                title="Rank the countries in this event"
              >
                {strokeLabel(r.style)} {distanceLabel(r.distance)}
              </a>
              {relay && <span className="rk-head__tag">relay</span>}
            </span>

            <span className="rk-cell rk-cell--time" role="cell">
              <UI_SwimTime
                time={r.time}
                quality={quality}
                marker="chip"
                chipSize="sm"
                className="rk-time src-rk-world-list"
              />
            </span>

            <span className="rk-cell rk-cell--holder" role="cell">
              {holderLabel(r)
                ? <bdi>{holderLabel(r)}</bdi>
                : <span className="rk-dash" title="The source publishes this record without a name">—</span>}
            </span>

            <span className="rk-cell rk-cell--country" role="cell">
              {r.holder_country ? (
                <>
                  <UI_FlagEmoji
                    countryCode={r.holder_country}
                    size="24x18"
                    className="rk-flag src-rk-world-list"
                  />
                  <span className="rk-code">{r.holder_country}</span>
                </>
              ) : <span className="rk-dash">—</span>}
            </span>

            <span className="rk-cell rk-cell--date" role="cell">
              {r.record_date || <span className="rk-dash">—</span>}
            </span>
          </div>
        );
      })}
    </div>
  );
};

export default RkWorldList;
