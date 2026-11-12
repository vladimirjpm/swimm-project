import React from 'react';
import { IconSnowflake, IconSun } from '../../../competitions-project/calendar/calendar-icons';
import { POSTER_COLOR } from '../poster-theme';

/**
 * Крупный значок сезона рядом с годом (раскладка C) — те же рисунки календаря, что стоят на
 * месте несодержательной плитки соревнования (`CompetitionTile`): один набор на все места.
 * Эмодзи плитки сюда не годится: у каждой системы оно своё. Сезон неизвестен — ничего.
 */
export default function SeasonIcon({ season, size, style }: { season: string | null; size: number; style?: React.CSSProperties }) {
  if (!season) return null;
  const winter = season === '❄️';
  const Icon = winter ? IconSnowflake : IconSun;
  return (
    <span role="img" aria-label={winter ? 'Winter season' : 'Summer season'} style={{ display: 'flex', color: winter ? POSTER_COLOR.sky : POSTER_COLOR.gold, ...style }}>
      <Icon size={size} />
    </span>
  );
}
