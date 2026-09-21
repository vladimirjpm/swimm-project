import React, { useMemo } from 'react';
import UI_H2HEventCard from '../../components/mix/h2h/h2h-event-card';
import UI_RecordBadge from '../../components/mix/record-badge/record-badge';
import UI_H2HPoolRow from '../../components/mix/h2h/h2h-pool-row';
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
 *
 * Рисуется КАРТОЧКОЙ семьи `UI_H2H*` в варианте `record` — той же, что секция официальных
 * рекордов на странице пловца (20.09.2026): там слева рекорд пловца, здесь рекорд Израиля,
 * справа в обоих случаях мировой мастерс. Своей вёрстки сравнения у страницы больше нет.
 * Отличий от страницы пловца два, и оба от того, что дисциплина тут ОДНА на весь экран:
 * карточка без шапки (стиль и дистанция — в полосе фильтров, `RkFilterBar`), а середина
 * строки печатает возрастную полосу вместо метки бассейна.
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

/** Строки таблицы: объединение полос двух сторон по одной дисциплине, по возрасту. */
function buildRows(israel: RegionRecord[], world: RegionRecord[], f: RkFilters): BandRow[] {
  const same = (r: RegionRecord) =>
    r.style === f.stroke && r.distance === f.distance
    && r.gender === f.gender && r.pool_type === f.poolType;

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
}

/**
 * Возрастные группы дисциплины для фильтра «Age group» в общей карточке. Считаются тем же
 * `buildRows`, что и таблица, — иначе кнопки и строки разъедутся.
 */
export function mastersBands(
  israel: RegionRecord[] | null | undefined,
  world: RegionRecord[] | null | undefined,
  f: RkFilters,
): string[] {
  if (!israel || !world) return [];
  return buildRows(israel, world, f).map((r) => r.band);
}

const RkMastersTable: React.FC<Props> = ({ israel, world, filters }) => {
  const rows = useMemo<BandRow[]>(
    () => buildRows(israel, world, filters),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [israel, world, filters.stroke, filters.distance, filters.gender, filters.poolType],
  );

  // Группа из адреса, которой у дисциплины нет, — показываем все, а не пустую таблицу.
  const activeBand = filters.ageGroup && rows.some((r) => r.band === filters.ageGroup)
    ? filters.ageGroup : null;
  const shown = activeBand ? rows.filter((r) => r.band === activeBand) : rows;

  if (rows.length === 0) {
    return <div className="rk-state">No masters records for this event.</div>;
  }

  const held = rows.filter((r) => r.israel).length;

  return (
    <>
      <div className="rk-count">
        Israel holds a record in {held} of {rows.length} age bands
      </div>

      {/* Контейнер `h2h-scope` обязателен: узкие ступени карточки — контейнерные запросы
          (`@container h2h`), и без него они не срабатывают вовсе. */}
      <div className="h2h-scope rk-bands">
        {/* Шапка колонок — та же `.h2h-group__head`, что над группами рекордов на странице
            пловца, но БЕЗ ступени: полоса тут у каждой строки своя и стоит в середине.
            Нужна за тем же, за чем там: без неё не сказать, чьё время слева, а чьё справа
            (просьба Влада 20.09.2026). */}
        <div className="h2h-group__head">
          <span><span aria-hidden="true">🏆 </span>{HOME_REGION} · MASTERS</span>
          <UI_RecordBadge kind="masters" />
          <span className="h2h-group__line" />
          <span className="h2h-group__wr">MASTERS WR</span>
        </div>

        {/* ОДНА карточка на дисциплину, строка — возрастная полоса. Шапки у неё нет
            (`head={false}`): стиль и дистанция общие на всю страницу и названы в полосе
            фильтров сверху — иконка над каждой из шестнадцати полос повторяла бы одно и
            то же (решение Влада 20.09.2026). */}
        <UI_H2HEventCard
          variant="record"
          stroke={filters.stroke}
          distance={filters.distance ?? ''}
          head={false}
        >
          {shown.map((row) => {
            const ahead = row.behindMs != null && row.behindMs < 0;
            const qIsr = row.israel?.issue_reason
              ? { kind: 'record' as const, reason: row.israel.issue_reason } : null;
            const qWorld = row.world?.issue_reason
              ? { kind: 'record' as const, reason: row.world.issue_reason } : null;

            return (
              <UI_H2HPoolRow
                key={row.band}
                poolType={filters.poolType ?? ''}
                // Различает строки ПОЛОСА, а не бассейн: бассейн выбран фильтром и во всех
                // строках один.
                midLabel={(
                  <span className="rk-band">
                    <span className="rk-band__label">Age Group</span>
                    <span className="rk-band__value">[{row.band}]</span>
                  </span>
                )}
                deltaMs={row.behindMs}
                // Израиль быстрее мирового своей полосы — аномалия справочника, но показываем
                // её как есть (см. шапку файла). Тон `win` честно говорит, в чью пользу цифра.
                deltaTone={ahead ? 'win' : 'behind'}
                left={row.israel ? {
                  time: row.israel.time,
                  date: row.israel.record_date,
                  quality: qIsr,
                  who: { name: holderLabel(row.israel) ?? '—', countryCode: HOME_REGION },
                  // Плашка всегда у домашней стороны: это её рекорд, а не победа в сравнении.
                  // Без заливки и рамки — см. `box` у UI_H2HTimeCell.
                  isWinner: true,
                  box: 'none' as const,
                  title: ahead ? AHEAD_TITLE : `${HOME_REGION} masters record · ${row.band}`,
                } : null}
                right={row.world ? {
                  time: row.world.time,
                  date: row.world.record_date,
                  quality: qWorld,
                  who: {
                    name: holderLabel(row.world) ?? '—',
                    countryCode: row.world.holder_country,
                  },
                  isWinner: false,
                  title: `Masters world record · ${row.band}`,
                } : null}
              />
            );
          })}
        </UI_H2HEventCard>
      </div>
    </>
  );
};

export default RkMastersTable;
