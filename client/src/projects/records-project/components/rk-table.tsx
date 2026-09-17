import React from 'react';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import type { RecordsRankingRow } from '../../../hooks/useRecordsRanking';
import { HOME_REGION, behindLabel } from '../rk-disciplines';

/**
 * Таблица рейтинга: страна, её рекорд, отставание от мирового, держатель и дата.
 *
 * Одна разметка на десктоп и мобайл: колонок пять, и на узком экране они складываются
 * гридом (см. `records-page.css`), а не переписываются вторым компонентом. Отдельные
 * `*-mobile`/`*-desktop` завели бы два места для одного правила подсветки.
 */

interface Props {
  rows: RecordsRankingRow[];
  /** Страна из адреса — подсвечивается дополнительно к домашней. */
  highlight?: string | null;
}

/**
 * Отставание отрицательное — рекорд страны быстрее мирового. Показываем как аномалию,
 * потому что рейтинг, где страна молча стоит выше мирового рекорда, читается как сломанный.
 *
 * ⚠ Но это НЕ обязательно ошибка справочника, и раньше подпись утверждала обратное
 * («два файла источника спорят»). Разбор 17.09.2026 (И-23): World Aquatics ведёт мировые
 * рекорды эстафет 4×50 только с 2013 года, поэтому заплыв 2008-го может быть быстрее
 * мирового рекорда и при этом быть честной строкой — обеими честными строками.
 * Единственный живой случай на всю базу — Франция, 4×50 в/с м 25 м (1:20.77 против 1:21.80).
 * Ошибка справочника — тоже возможная причина (И-22), поэтому текст называет обе и не
 * берётся решать, какая здесь.
 */
const AHEAD_TITLE =
  'Faster than the world record listed for this event. Not an error by itself — World Aquatics '
  + 'has only ratified 4×50 relay records since 2013, so the best swim ever can be older than '
  + 'the record list. We publish both rows exactly as the source gives them.';

const RkTable: React.FC<Props> = ({ rows, highlight }) => (
  <div className="rk-table" role="table" aria-label="Country records ranking">
    <div className="rk-row rk-row--head" role="row">
      <span role="columnheader">#</span>
      <span role="columnheader">Country</span>
      <span role="columnheader">Time</span>
      <span role="columnheader">Holder</span>
      <span role="columnheader">Date</span>
    </div>

    {rows.map((row) => {
      const home = row.region_code === HOME_REGION;
      const picked = !!highlight && row.region_code === highlight;
      const ahead = row.behind_world_ms != null && row.behind_world_ms < 0;
      const quality = row.issue_reason ? { kind: 'record' as const, reason: row.issue_reason } : null;
      const flagged = swimFlaggedRowProps(quality);

      const classes = [
        'rk-row',
        home ? 'rk-row--home' : '',
        picked ? 'rk-row--picked' : '',
        ahead ? 'rk-row--ahead' : '',
        flagged.className ?? '',
      ].filter(Boolean).join(' ');

      return (
        <div
          key={row.region_code}
          role="row"
          className={classes}
          title={flagged.title ?? (ahead ? AHEAD_TITLE : undefined)}
        >
          <span className="rk-cell rk-cell--rank" role="cell">{row.rank}</span>

          <span className="rk-cell rk-cell--country" role="cell">
            <UI_FlagEmoji
              countryCode={row.region_code}
              size="24x18"
              className="rk-flag src-rk-table"
            />
            <span className="rk-code">{row.region_code}</span>
          </span>

          <span className="rk-cell rk-cell--time" role="cell">
            <UI_SwimTime
              time={row.time}
              quality={quality}
              marker="chip"
              chipSize="sm"
              className="rk-time src-rk-table"
            />
            {behindLabel(row.behind_world_ms) && (
              <span
                className={`rk-behind${ahead ? ' rk-behind--ahead' : ''}`}
                title={ahead ? AHEAD_TITLE : 'Behind the world record'}
              >
                {behindLabel(row.behind_world_ms)}
              </span>
            )}
          </span>

          <span className="rk-cell rk-cell--holder" role="cell">
            {row.holder_name?.trim()
              // Источник отдаёт часть рекордов (почти все эстафетные) без имён. Прочерк, а не
              // пустое место: пустота читается как «не успело загрузиться».
              || <span className="rk-dash" title="The source publishes this record without a name">—</span>}
          </span>

          <span className="rk-cell rk-cell--date" role="cell">
            {row.record_date || <span className="rk-dash">—</span>}
          </span>
        </div>
      );
    })}
  </div>
);

export default RkTable;
