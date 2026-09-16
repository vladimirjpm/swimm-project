import React from 'react';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import type { RecordCompareRow, RecordCompareSide } from '../../../hooks/useRecordsCompare';
import { distanceLabel, strokeLabel } from '../rk-disciplines';

/**
 * Таблица сравнения: дисциплина, время каждой стороны с датой, разница.
 *
 * ⚠ Сторона без рекорда — **прочерк и подпись «no record»**, а не пустая ячейка и не ноль.
 * Пустая ячейка читается как «не загрузилось», ноль — как «ноль секунд», а правило этапа
 * (11.3.3) требует, чтобы «нет данных» отличалось от «медленнее» глазами, а не только в JSON.
 */

interface Props {
  rows: RecordCompareRow[];
  a: string;
  b: string;
}

const NO_DATA_TITLE =
  'One of the countries has no record in this event — nothing to compare. '
  + 'It counts for neither side.';

/** Ячейка одной стороны: время с пометкой качества и дата под ним. */
const Side: React.FC<{ side?: RecordCompareSide | null; won: boolean }> = ({ side, won }) => {
  if (!side) {
    return (
      <span className="rc-side rc-side--empty" title={NO_DATA_TITLE}>
        <span className="rc-dash">—</span>
        <span className="rc-side__date">no record</span>
      </span>
    );
  }

  const quality = side.issue_reason ? { kind: 'record' as const, reason: side.issue_reason } : null;

  return (
    <span className={`rc-side${won ? ' rc-side--won' : ''}`}>
      <UI_SwimTime
        time={side.time}
        quality={quality}
        marker="chip"
        chipSize="sm"
        className="rc-side__time src-rc-table"
      />
      {/* Дата обязательна рядом со временем: иначе сравниваются отметки разных эпох,
          и рекорд 2002 года читается просто как «медленнее» (11.3.3). */}
      <span className="rc-side__date">{side.record_date || '—'}</span>
    </span>
  );
};

const RcTable: React.FC<Props> = ({ rows, a, b }) => (
  <div className="rc-table" role="table" aria-label="Country records comparison">
    <div className="rc-row rc-row--head" role="row">
      <span role="columnheader">Event</span>
      <span role="columnheader">{a}</span>
      <span role="columnheader">{b}</span>
      <span role="columnheader">Diff</span>
    </div>

    {rows.map((row) => {
      const key = `${row.style}|${row.distance}|${row.gender}|${row.pool_type}`;
      const noData = row.outcome === 'no_data';
      const flagged = swimFlaggedRowProps(
        row.a?.issue_reason || row.b?.issue_reason
          ? { kind: 'record', reason: row.a?.issue_reason ?? row.b?.issue_reason }
          : null,
      );

      const classes = ['rc-row', noData ? 'rc-row--nodata' : '', flagged.className ?? '']
        .filter(Boolean).join(' ');

      return (
        <div key={key} role="row" className={classes} title={flagged.title ?? (noData ? NO_DATA_TITLE : undefined)}>
          <span className="rc-cell rc-cell--event" role="cell">
            <span className="rc-event__main">
              {distanceLabel(row.distance)} {strokeLabel(row.style).toLowerCase()}
            </span>
            <span className="rc-event__sub">
              {row.gender === 'female' ? 'women' : 'men'} · {row.pool_type}
            </span>
          </span>

          <span className="rc-cell" role="cell">
            <Side side={row.a} won={row.outcome === 'a'} />
          </span>

          <span className="rc-cell" role="cell">
            <Side side={row.b} won={row.outcome === 'b'} />
          </span>

          <span className="rc-cell rc-cell--diff" role="cell">
            {row.delta_ms == null
              ? <span className="rc-dash" title={NO_DATA_TITLE}>—</span>
              : row.delta_ms === 0
                ? <span className="rc-diff rc-diff--tie">tie</span>
                : <span className="rc-diff">{(row.delta_ms / 1000).toFixed(2)}</span>}
          </span>
        </div>
      );
    })}
  </div>
);

export default RcTable;
