import React from 'react';
import SwimRow from '../../components/swim-row/swim-row';
import type { SwimQualityDto, CompetitionRef } from '../use-swimmer-page';
import { routes } from '../../../utils/routes';

/**
 * Строка результата страницы спортсмена — ПЕРЕХОДНИК к общему `SwimRow`
 * (docs/plans/swim-row-shared-component-plan.md).
 *
 * Своей вёрстки здесь больше нет: две линии рисует общий компонент, тот же, что в карточке
 * спортсмена и в списке `/season-best`. Файл остался ради того, что принадлежит ИМЕННО этой
 * странице и не должно уезжать в общий компонент:
 *  • NAV-контракт `swimHref` — адрес заплыва в таблице результатов;
 *  • раскладка `ResultRowData` (форма ответа `/best-times` и `/progress`) по пропам строки.
 * Оба таба, Results и Progress, зовут его, поэтому копия этой раскладки была бы вторым
 * местом, где живёт одно и то же.
 *
 * Что поменялось при переезде (осознанно):
 *  • дисциплину рисует иконка стиля с красной дистанцией вместо плиты `EventPlate`
 *    (решение Влада §7 п.1);
 *  • сплиты уехали под время сворачиваемым блоком — как в карточке спортсмена;
 *  • у помеченного «под вопросом» заплыва больше нет дуги уровня: он не показывается как
 *    достижение (правило `UI_SwimTime`), а бейджа у него не было и раньше.
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
  /**
   * Мастерс-старт: у разряда своя таблица нормативов с возрастными полосами. Без него
   * время ветерана меряется юношеской шкалой и дуга уровня показывает чужой разряд.
   */
  isMasters?: boolean;
  /**
   * Чип у времени: BEST — лучшее в сезоне, PB — личный рекорд на графике прогресса,
   * SB — первое место среди сверстников сезона. SB сильнее BEST и ЗАМЕЩАЕТ его: одна
   * строка не носит два чипа, иначе «первый в группе» терялся бы среди BEST у всех строк.
   */
  badge?: 'best' | 'pb' | 'sb' | null;
}

/** Ссылка на конкретный заплыв в таблице результатов (NAV-контракт хендоффа). */
const swimHref = (row: ResultRowData, swimmerId: number) =>
  routes.competitionSwims(row.competition.id, { swimmerId, resultId: row.resultId });

interface Props {
  row: ResultRowData;
  swimmerId: number;
  /** Нормативы у мужчин и женщин разные — без пола дуга уровня врёт. */
  gender: 'male' | 'female';
}

function SwimmerResultRow({ row, swimmerId, gender }: Props) {
  // Prelim-место — ранжир сессии, не медаль (Р34): кружок вместо медали.
  const isMedal =
    row.place != null && row.place >= 1 && row.place <= 3 && row.heatType !== 'prelim';

  return (
    <SwimRow
      className="deep-swim-row"
      href={swimHref(row, swimmerId)}
      stroke={row.stroke ?? ''}
      distance={row.distance}
      poolType={row.poolType}
      time={row.time}
      quality={row.quality}
      splits={row.splits}
      badge={row.badge ?? null}
      heatType={row.heatType}
      place={{
        kind: isMedal ? 'medal' : 'circle',
        value: row.place,
        isAward: isMedal,
        caption: row.ageInSeason != null ? `age ${row.ageInSeason}` : null,
      }}
      // Соревнование — во второй линии, рядом с датой: это ответ на «где и когда», а не
      // идентичность строки. В первой линии оно растягивало карточку и расталкивало
      // плитку и время по краям экрана (решение Влада 2026-08-27).
      competition={{ name: row.competition.name, isChampionship: row.competition.isChampionship }}
      meetPlacement="line2"
      date={row.date}
      points={row.points ?? null}
      level={{ gender, ageInSeason: row.ageInSeason, isMasters: !!row.isMasters }}
    />
  );
}

export default SwimmerResultRow;
