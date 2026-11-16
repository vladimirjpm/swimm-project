import React from 'react';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';
import { routes } from '../../../utils/routes';
import type { SeasonBestListItem } from '../../../hooks/useSeasonBestList';
import { deltaLabel, type SbPreviousLine } from '../sb-previous-best-model';

/**
 * Карточка над списком `/season-best`: лучшее время ПРОШЛОГО сезона в этой же категории и
 * насколько от него ушёл нынешний лидер. Одна строка на пол (пол не выбран — две).
 *
 * Сравниваются ступени, не люди: «10 лет» прошлого сезона — родившиеся на год раньше
 * нынешних десятилеток (ось возраста сезонная). Расчёт — sb-previous-best-model.ts.
 */
interface Props {
  /** Подпись прошлого сезона: «2025/26». */
  seasonLabel: string;
  lines: SbPreviousLine<SeasonBestListItem>[];
}

const GENDER_MARK: Record<'male' | 'female', string> = { male: '♂', female: '♀' };

export default function SbPreviousBest({ seasonLabel, lines }: Props) {
  if (lines.length === 0) return null;

  return (
    <div className="sb-prev">
      <div className="sb-prev__title">Last season best · {seasonLabel}</div>
      {lines.map(({ gender, previous, deltaMs }) => (
        <div key={gender} className={`sb-prev__row sb-prev__row--${gender}`}>
          <span className="sb-prev__gender" aria-label={gender}>{GENDER_MARK[gender]}</span>
          <span className="sb-prev__time"><UI_SwimTime time={previous.time} /></span>
          <span className="sb-prev__who">
            <a className="sb-prev__name" href={routes.swimmer(previous.swimmer_id)}>{previous.name}</a>
            {previous.club && <span className="sb-prev__club">{previous.club}</span>}
            <span className="sb-prev__date">{previous.date}</span>
          </span>
          {deltaMs != null && (
            <span
              className={`sb-prev__delta${deltaMs < 0 ? ' sb-prev__delta--faster' : deltaMs > 0 ? ' sb-prev__delta--slower' : ''}`}
              title={deltaMs < 0
                ? 'This season\'s best is faster'
                : deltaMs > 0 ? 'This season\'s best is slower' : 'Same as this season\'s best'}
            >
              now {deltaLabel(deltaMs)}
            </span>
          )}
        </div>
      ))}
    </div>
  );
}
