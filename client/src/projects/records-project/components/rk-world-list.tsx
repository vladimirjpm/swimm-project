import React, { useMemo } from 'react';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import type { RegionRecord } from '../../../hooks/useRegionRecords';
import { routes } from '../../../utils/routes';
import { timeToMs } from '../../../utils/helpers/recalculate-positions';
import {
  RK_STROKES, behindLabel, distanceLabel, holderLabel, strokeLabel, type RkFilters,
} from '../rk-disciplines';

/**
 * Таб «World records»: все мировые рекорды одного бассейна и пола списком — или, если в табе
 * выбрана страна (9.9), все её национальные рекорды с отставанием от мирового.
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
  /**
   * Мировые рекорды — только когда `records` это национальные рекорды СТРАНЫ (9.9, выбор
   * региона в табе). Тогда колонка «Nation» (у всех строк одна и та же) становится «vs WR»:
   * отставание от мирового той же дисциплины. null — список и есть мировые рекорды.
   */
  world?: RegionRecord[] | null;
}

/** Ключ дисциплины внутри бассейна и пола — им национальный рекорд ищет свой мировой. */
const disciplineKey = (r: RegionRecord) => `${r.pool_type}|${r.gender}|${r.style}|${r.distance}`;

/** Ключ дисциплины для порядка программы: индекс стиля, затем индекс дистанции в нём. */
function programmeOrder(style: string, distance: string): number {
  const si = RK_STROKES.findIndex((s) => s.key === style);
  if (si < 0) return Number.MAX_SAFE_INTEGER;
  const stroke = RK_STROKES[si];
  const all = [...stroke.distances, ...stroke.relays];
  const di = all.indexOf(distance);
  return si * 100 + (di < 0 ? 99 : di);
}

const RkWorldList: React.FC<Props> = ({ records, filters, world = null }) => {
  const rows = useMemo(
    () => records
      .filter((r) => r.pool_type === filters.poolType && r.gender === filters.gender)
      .sort((a, b) => programmeOrder(a.style, a.distance) - programmeOrder(b.style, b.distance)),
    [records, filters.poolType, filters.gender],
  );

  const worldByKey = useMemo(
    () => (world ? new Map(world.map((w) => [disciplineKey(w), w])) : null),
    [world],
  );
  const national = worldByKey != null;
  const rowClass = `rk-row rk-row--wr${national ? ' rk-row--nr' : ''}`;

  if (rows.length === 0) {
    return (
      <div className="rk-state">
        {national ? 'No national records for this pool and gender.' : 'No world records for this pool and gender.'}
      </div>
    );
  }

  return (
    <div className="rk-table" role="table" aria-label={national ? 'National records' : 'World records'}>
      <div className={`${rowClass} rk-row--head`} role="row">
        <span role="columnheader">Event</span>
        <span role="columnheader">Time</span>
        <span role="columnheader">Holder</span>
        <span role="columnheader">{national ? 'vs WR' : 'Nation'}</span>
        <span role="columnheader">Date</span>
      </div>

      {rows.map((r) => {
        const quality = r.issue_reason ? { kind: 'record' as const, reason: r.issue_reason } : null;
        const flagged = swimFlaggedRowProps(quality);
        const wr = worldByKey?.get(disciplineKey(r)) ?? null;
        // Разрыв на клиенте, как в карточке рекорда пловца: у справочника миллисекунд нет.
        const gapMs = wr ? timeToMs(r.time) - timeToMs(wr.time) : null;
        const gap = gapMs != null && Number.isFinite(gapMs) ? gapMs : null;

        return (
          <div
            key={`${r.style}|${r.distance}`}
            role="row"
            className={[rowClass, flagged.className ?? ''].filter(Boolean).join(' ')}
            title={flagged.title}
          >
            <span className="rk-cell rk-cell--event" role="cell">
              <a
                className="rc-link"
                href={routes.records({
                  stroke: r.style, distance: r.distance,
                  gender: filters.gender, poolType: filters.poolType,
                  // Из списка страны — в рейтинг с её строкой подсвеченной: «а она где?».
                  highlight: national ? r.region_code : null,
                })}
                title={`${strokeLabel(r.style)} ${distanceLabel(r.distance)} — rank the countries in this event`}
                aria-label={`${strokeLabel(r.style)} ${distanceLabel(r.distance)}`}
              >
                {/* Дисциплина — общим значком стиля с дистанцией справа, как в строке заплыва и
                    карточках рекордов; текст — в подсказке и aria-label. Белая плита под
                    иконкой обязательна: PNG стилей нарисованы под светлый фон. */}
                <span className="rk-event-plate">
                  <UI_SwimmStyleIcon
                    styleName={r.style}
                    styleLen={r.distance.replace(/m$/i, '')}
                    styleType="icon-len"
                    lenPlacement="right"
                    size={48}
                    lenSize={16}
                  />
                </span>
              </a>
              {/* Бейджа «relay» нет: «4X100» на значке уже говорит, что это эстафета, а на
                  телефоне бейдж наезжал на время. */}
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

            {national ? (
              <span className="rk-cell rk-cell--country rk-cell--gap" role="cell">
                {behindLabel(gap) ? (
                  <span
                    className={`rk-behind${gap! < 0 ? ' rk-behind--ahead' : ''}`}
                    title={wr ? `World record ${wr.time}` : undefined}
                  >
                    {behindLabel(gap)}
                  </span>
                ) : <span className="rk-dash" title="No world record for this event">—</span>}
              </span>
            ) : (
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
            )}

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
