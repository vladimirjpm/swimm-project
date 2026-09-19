import React, { useMemo } from 'react';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import type { RegionRecord } from '../../../hooks/useRegionRecords';
import HelperTime from '../../../utils/helpers/helper-time';
import {
  HOME_REGION, bandStart, behindLabel, holderLabel, type RkFilters,
} from '../rk-disciplines';

/**
 * Таб «Masters»: мастерский рекорд Израиля против мирового рекорда ТОЙ ЖЕ полосы, по всем
 * полосам одной дисциплины.
 *
 * Ось «одна дисциплина — все полосы» выбрана не на вкус: страница уже устроена вокруг
 * одной дисциплины (пикер сверху), и так мастерс видит свою дистанцию через всю жизнь, от
 * 25-29 до 90-94. Сравнение возможно только так — мастерских национальных рекордов у других
 * стран нет вовсе (решение 29.07.2026), поэтому ни рейтинга, ни head-to-head стран здесь нет.
 *
 * Полосы — объединение двух сторон: у мира они доходят до 105-109, у Израиля кончаются на
 * 90-94. Строка, где рекорд есть только у одной стороны, остаётся с прочерком — пустая клетка
 * честнее отката на абсолютный рекорд, который в этой полосе ничего не значит.
 *
 * ⚠ Рекорд Израиля БЫСТРЕЕ мирового своей полосы — аномалия справочника (сторож импорта ловит
 * это правилом `faster-than-world-record` с планкой полосы). Показываем, а не прячем: так же,
 * как рейтинг стран показывает отрицательное отставание.
 */

interface Props {
  israel: RegionRecord[];
  world: RegionRecord[];
  filters: RkFilters;
}

interface BandRow {
  band: string;
  israel?: RegionRecord;
  world?: RegionRecord;
  /** Отставание Израиля от мира в мс; null — одной из сторон нет. */
  behindMs: number | null;
}

function toMs(time: string): number | null {
  const s = HelperTime.parseTimeToSeconds(time);
  return Number.isFinite(s) ? Math.round(s * 1000) : null;
}

const AHEAD_TITLE =
  'Faster than the masters world record of the same age band. The source lists both rows; '
  + 'one of them is likely wrong — we publish them exactly as given.';

const RkMastersTable: React.FC<Props> = ({ israel, world, filters }) => {
  const rows = useMemo<BandRow[]>(() => {
    const same = (r: RegionRecord) =>
      r.style === filters.stroke && r.distance === filters.distance
      && r.gender === filters.gender && r.pool_type === filters.poolType;

    const byBand = new Map<string, BandRow>();
    const slot = (band: string) => {
      let row = byBand.get(band);
      if (!row) { row = { band, behindMs: null }; byBand.set(band, row); }
      return row;
    };

    israel.filter(same).forEach((r) => { slot(r.age_key).israel = r; });
    world.filter(same).forEach((r) => { slot(r.age_key).world = r; });

    return [...byBand.values()]
      .map((row) => {
        const a = row.israel ? toMs(row.israel.time) : null;
        const b = row.world ? toMs(row.world.time) : null;
        return { ...row, behindMs: a != null && b != null ? a - b : null };
      })
      .sort((x, y) => bandStart(x.band) - bandStart(y.band));
  }, [israel, world, filters.stroke, filters.distance, filters.gender, filters.poolType]);

  if (rows.length === 0) {
    return <div className="rk-state">No masters records for this event.</div>;
  }

  const held = rows.filter((r) => r.israel).length;

  return (
    <>
      <div className="rk-count">
        Israel holds a record in {held} of {rows.length} age bands
      </div>

      <div className="rk-table" role="table" aria-label="Masters records by age band">
        <div className="rk-row rk-row--masters rk-row--head" role="row">
          <span role="columnheader">Band</span>
          <span role="columnheader">Israel</span>
          <span role="columnheader">World</span>
          <span role="columnheader">Gap</span>
        </div>

        {rows.map((row) => {
          const ahead = row.behindMs != null && row.behindMs < 0;
          const qIsr = row.israel?.issue_reason ? { kind: 'record' as const, reason: row.israel.issue_reason } : null;
          const qWorld = row.world?.issue_reason ? { kind: 'record' as const, reason: row.world.issue_reason } : null;
          const flagged = swimFlaggedRowProps(qIsr ?? qWorld);

          return (
            <div
              key={row.band}
              role="row"
              className={[
                'rk-row', 'rk-row--masters',
                ahead ? 'rk-row--ahead' : '',
                flagged.className ?? '',
              ].filter(Boolean).join(' ')}
              title={flagged.title ?? (ahead ? AHEAD_TITLE : undefined)}
            >
              <span className="rk-cell rk-cell--band" role="cell">{row.band}</span>

              <MastersSide record={row.israel} quality={qIsr} label={HOME_REGION}
                emptyTitle={`No ${HOME_REGION} record in this band`} />
              <MastersSide record={row.world} quality={qWorld} label="World"
                emptyTitle="No world record kept for this band" />

              <span className="rk-cell rk-cell--gap" role="cell">
                {behindLabel(row.behindMs) ? (
                  <span
                    className={`rk-behind${ahead ? ' rk-behind--ahead' : ''}`}
                    title={ahead ? AHEAD_TITLE : 'Behind the world record of this band'}
                  >
                    {behindLabel(row.behindMs)}
                  </span>
                ) : <span className="rk-dash">—</span>}
              </span>
            </div>
          );
        })}
      </div>
    </>
  );
};

/**
 * Одна сторона сравнения: время, под ним держатель и дата.
 *
 * `label` виден только на мобайле: там шапки таблицы нет, стороны встают друг под другом,
 * и без метки не понять, где Израиль, а где мир.
 */
const MastersSide: React.FC<{
  record?: RegionRecord;
  quality: { kind: 'record'; reason: string } | null;
  label: string;
  emptyTitle: string;
}> = ({ record, quality, label, emptyTitle }) => {
  const tag = <span className="rk-side__tag">{label}</span>;
  if (!record) {
    return (
      <span className="rk-cell rk-cell--side" role="cell">
        <span className="rk-side__line">
          {tag}
          <span className="rk-dash" title={emptyTitle}>—</span>
        </span>
      </span>
    );
  }
  return (
    <span className="rk-cell rk-cell--side" role="cell">
      <span className="rk-side__line">
        {tag}
        <UI_SwimTime
          time={record.time}
          quality={quality}
          marker="chip"
          chipSize="sm"
          className="rk-time src-rk-masters-table"
        />
      </span>
      {/* <bdi> обязателен: ивритское имя рядом с датой без изоляции переставляет строку
          («07/09/2010 · זילברמן גלעד» вместо «זילברמן גלעד · 07/09/2010»). */}
      <span className="rk-side__who">
        <bdi>{holderLabel(record) || '—'}</bdi>
        {record.record_date && <span className="rk-side__date"> · {record.record_date}</span>}
      </span>
    </span>
  );
};

export default RkMastersTable;
