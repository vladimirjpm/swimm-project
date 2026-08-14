import React from 'react';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import UI_PrelimLabel from '../../components/mix/prelim-label/prelim-label';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import Helper from '../../../utils/helpers/data-helper';
import type { SwimQualityDto, CompetitionRef } from '../use-swimmer-page';

/**
 * Строка результата страницы спортсмена (BLOCKS.md §6) — общая для табов Results и Progress.
 *
 * Линия 1: место + возраст · плита стиля с дистанцией · соревнование · время.
 * Линия 2: сплиты · очки · дата, справа дуга уровня.
 *
 * Правила, которые нельзя потерять:
 *  • время выводится ТОЛЬКО через UI_SwimTime, носитель спорной строки — swimFlaggedRowProps
 *    (иначе признак качества пропадёт на новом экране — ради этого шов и заводили);
 *  • у соревнования всегда есть название, у чемпионата перед ним 🏆 (по флагу с сервера,
 *    а не по разбору названия);
 *  • вся строка — настоящая ссылка на заплыв в таблице результатов.
 */

export interface ResultRowData {
  stroke?: string | null;
  distance: string;
  poolType: string;
  time?: string | null;
  quality?: SwimQualityDto | null;
  points?: number | null;
  place?: number | null;
  /** 'prelim' | 'final' | null — место prelim-заплыва рисуется без медали. */
  heatType?: string | null;
  ageInSeason?: number | null;
  splits?: string | null;
  date: string;
  competition: CompetitionRef;
  resultId: number;
  /** Чип BEST — только в табе Results (в Progress истории личников своя разметка). */
  badge?: 'best' | 'pb' | null;
}

/** Ссылка на конкретный заплыв в таблице результатов (NAV-контракт хендоффа). */
const swimHref = (row: ResultRowData, swimmerId: number) =>
  `/results?competitionId=${row.competition.id}&tab=swims&swimmerId=${swimmerId}&resultId=${row.resultId}`;

/** «2026-04-27» → «27 APR 2026». Строка приходит ISO, отображение — как в макете. */
const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
function formatDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return iso;
  return `${String(d).padStart(2, '0')} ${MONTHS[m - 1]} ${y}`;
}

interface Props {
  row: ResultRowData;
  swimmerId: number;
  /** Нормативы у мужчин и женщин разные — без пола дуга уровня врёт. */
  gender: 'male' | 'female';
}

function SwimmerResultRow({ row, swimmerId, gender }: Props) {
  const flagged = swimFlaggedRowProps(row.quality);

  // Уровень считает клиент из NormativeStandard — вторая реализация на сервере
  // разъехалась бы с этой (правило плана §2).
  const levelInfo = row.time
    ? Helper.getNormativeLevelInfo({
        gender,
        poolType: Helper.resolvePoolType(row.poolType),
        styleName: row.stroke ?? '',
        distance: `${row.distance}m`,
        time: Helper.parseTimeToSeconds(row.time),
      })
    : null;

  return (
    <a
      {...flagged}
      href={swimHref(row, swimmerId)}
      className={`deep-result-row${flagged.className ? ` ${flagged.className}` : ''}`}
    >
      <div className="deep-result-row__line1">
        <div className="deep-result-row__place">
          {/* Prelim-место — ранжир сессии, не медаль (Р34): кружок вместо медали. */}
          {row.place != null && row.place >= 1 && row.place <= 3 && row.heatType !== 'prelim' ? (
            <UI_MedalIcon place={String(row.place)} styleType="icon-place" styleSize="medal-40" />
          ) : (
            <span className="deep-place-circle">{row.place ?? '—'}</span>
          )}
          <UI_PrelimLabel heatType={row.heatType} className="deep-result-row__age" />
          {row.ageInSeason != null && <span className="deep-result-row__age">age {row.ageInSeason}</span>}
        </div>

        <div className="deep-stroke-plate">
          <UI_SwimmStyleIcon styleName={row.stroke ?? ''} styleType="icon-notext" />
          <span className="deep-stroke-plate__dist">{row.distance}</span>
        </div>

        <div className="deep-result-row__meet">
          <div className="deep-result-row__meet-name" dir="auto">
            {row.competition.isChampionship && <span aria-hidden="true">🏆 </span>}
            {row.competition.name}
          </div>
          <div className="deep-result-row__meet-sub">{row.poolType} pool</div>
        </div>

        <div className="deep-result-row__time">
          <UI_SwimTime
            time={row.time ?? '—'}
            quality={row.quality}
            marker="chip"
            chipSize="sm"
            className="deep-result-row__time-value"
          />
          {row.badge === 'best' && !row.quality && <span className="deep-chip-best">BEST</span>}
          {row.badge === 'pb' && !row.quality && <span className="deep-chip-best">PB</span>}
        </div>
      </div>

      <div className="deep-result-row__line2">
        <span className="deep-result-row__splits">{row.splits || ''}</span>
        <span className="deep-result-row__points">
          Points: {row.points != null ? row.points : '—'}
        </span>
        <span className="deep-result-row__date">{formatDate(row.date)}</span>
        <span className="deep-result-row__level">
          {levelInfo && levelInfo.currentLevel !== 'none' && !row.quality && (
            <UI_NormativeLevelIcon
              levelName={levelInfo.currentLevel}
              styleType="gauge"
              styleSize="size-2"
              styleName={row.stroke ?? ''}
              styleLen={row.distance}
              poolType={row.poolType}
              progressPercent={levelInfo.progressToNextLevel}
              nextTime={levelInfo.nextTime}
              disableClick
            />
          )}
        </span>
      </div>
    </a>
  );
}

export default SwimmerResultRow;
