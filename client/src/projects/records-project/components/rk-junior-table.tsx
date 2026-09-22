import React, { useMemo } from 'react';
import UI_H2HEventCard from '../../components/mix/h2h/h2h-event-card';
import UI_RecordBadge from '../../components/mix/record-badge/record-badge';
import UI_H2HPoolRow from '../../components/mix/h2h/h2h-pool-row';
import type { RegionRecord } from '../../../hooks/useRegionRecords';
import HelperTime from '../../../utils/helpers/helper-time';
import { genderLabel, HOME_REGION, holderLabel, type RkFilters } from '../rk-disciplines';

/**
 * Таб «World Junior»: возрастной рекорд Израиля против мирового ЮНИОРСКОГО рекорда (WJR) той же
 * дисциплины (WJR-план J4, docs/plans/world-junior-records-plan.md; просьба Влада 21.09.2026).
 *
 * Брат таба Masters (`RkMastersTable`) и та же карточка `UI_H2H*` в варианте `record`, но с
 * одним отличием, которое экран обязан показать: WJR — не рекорд возраста, а рекорд ПОЛОСЫ
 * (женщины 14–17, мужчины 15–18, возраст на 31 декабря). Справа поэтому одно и то же время во
 * всех строках — это правда, а не ошибка: израильские рекорды 15 и 17 лет мерятся одним WJR.
 * Полоса названа в чипе «Age band» полосы фильтров и в строке-пояснении над карточкой.
 *
 * Строки — только возрасты ВНУТРИ полосы. Для 10–13 (и 18 у девушек, 14 у юношей)
 * официального мирового эталона нет в принципе, и строка с прочерком справа читалась бы как
 * «данных пока нет».
 */

interface Props {
  israel: RegionRecord[];
  world: RegionRecord[];
  filters: RkFilters;
}

interface AgeRow {
  age: number;
  israel?: RegionRecord;
  behindMs: number | null;
}

function toMs(time: string): number | null {
  const s = HelperTime.parseTimeToSeconds(time);
  return Number.isFinite(s) ? Math.round(s * 1000) : null;
}

/** «14-17» → [14, 17]; не полоса — null. */
function parseBand(band: string): [number, number] | null {
  const m = /^(\d+)-(\d+)$/.exec(band.trim());
  if (!m) return null;
  const lo = Number(m[1]);
  const hi = Number(m[2]);
  return lo <= hi ? [lo, hi] : null;
}

/** «14-17» → «14–17»: полоса читается диапазоном, а не вычитанием. */
const bandLabel = (band: string) => band.replace('-', '–');

const RkJuniorTable: React.FC<Props> = ({ israel, world, filters }) => {
  const same = (r: RegionRecord) =>
    r.style === filters.stroke && r.distance === filters.distance
    && r.gender === filters.gender && r.pool_type === filters.poolType;

  const wjr = useMemo(
    () => world.find(same) ?? null,
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [world, filters.stroke, filters.distance, filters.gender, filters.poolType],
  );
  const band = wjr ? parseBand(wjr.age_key) : null;

  const rows = useMemo<AgeRow[]>(() => {
    if (!wjr || !band) return [];
    const wjrMs = toMs(wjr.time);
    const out: AgeRow[] = [];
    for (let age = band[0]; age <= band[1]; age++) {
      const rec = israel.find((r) => same(r) && r.age_key === String(age));
      const a = rec ? toMs(rec.time) : null;
      out.push({ age, israel: rec, behindMs: a != null && wjrMs != null ? a - wjrMs : null });
    }
    return out;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [israel, wjr, filters.stroke, filters.distance, filters.gender, filters.poolType]);

  if (!wjr || !band) {
    return <div className="rk-state">No World Junior record for this event.</div>;
  }

  const who = genderLabel(filters.gender, true);
  const qWorld = wjr.issue_reason ? { kind: 'record' as const, reason: wjr.issue_reason } : null;
  const held = rows.filter((r) => r.israel).length;

  return (
    <>
      <div className="rk-count">
        {/* Главная оговорка экрана — одной строкой над карточкой, а не в title: без неё
            одинаковое время справа выглядит как баг. */}
        World Junior records are kept for one age band — {who.toLowerCase()} {bandLabel(wjr.age_key)} — not
        per age · Israel holds a record at {held} of {rows.length} ages
      </div>

      {/* Контейнер `h2h-scope` обязателен: узкие ступени карточки — контейнерные запросы. */}
      <div className="h2h-scope rk-bands">
        <div className="h2h-group__head">
          <span><span aria-hidden="true">🏆 </span>{HOME_REGION} · AGE</span>
          <UI_RecordBadge kind="age" />
          <span className="h2h-group__line" />
          {/* Полосы в шапке нет: она уже в чипе «Age band» и в строке над карточкой, а на
              телефоне «WORLD JUNIOR 15–18» упирается в край. Подпись та же, что на странице
              пловца. */}
          <span className="h2h-group__wr">WORLD JUNIOR</span>
        </div>

        <UI_H2HEventCard
          variant="record"
          stroke={filters.stroke}
          distance={filters.distance ?? ''}
          head={false}
        >
          {rows.map((row) => {
            const qIsr = row.israel?.issue_reason
              ? { kind: 'record' as const, reason: row.israel.issue_reason } : null;
            // Израиль быстрее WJR — не аномалия, а юниор с мировым уровнем (или ошибка
            // справочника). Показываем как есть, тон честно говорит, в чью пользу цифра.
            const ahead = row.behindMs != null && row.behindMs < 0;
            return (
              <UI_H2HPoolRow
                key={row.age}
                poolType={filters.poolType ?? ''}
                midLabel={(
                  <span className="rk-band">
                    <span className="rk-band__label">Age</span>
                    <span className="rk-band__value">[{row.age}]</span>
                  </span>
                )}
                deltaMs={row.behindMs}
                deltaTone={ahead ? 'win' : 'behind'}
                left={row.israel ? {
                  time: row.israel.time,
                  date: row.israel.record_date,
                  quality: qIsr,
                  who: { name: holderLabel(row.israel) ?? '—', countryCode: HOME_REGION },
                  isWinner: true,
                  box: 'none' as const,
                  title: `${HOME_REGION} age record · ${row.age}`,
                } : null}
                right={{
                  time: wjr.time,
                  date: wjr.record_date,
                  quality: qWorld,
                  who: { name: holderLabel(wjr) ?? '—', countryCode: wjr.holder_country },
                  isWinner: false,
                  title: `World Junior record · ${who.toLowerCase()} ${bandLabel(wjr.age_key)}`,
                }}
              />
            );
          })}
        </UI_H2HEventCard>
      </div>
    </>
  );
};

export default RkJuniorTable;
