import React from 'react';
import UI_SeasonIcon, { isSeason } from '../../../components/mix/season-icon/season-icon';
import { POSTER_COLOR } from '../poster-theme';

/**
 * Крупный значок сезона рядом с годом (раскладка C): общий `UI_SeasonIcon` в цветах макета —
 * зима светло-голубым, лето золотом. Сезон неизвестен — ничего.
 */
export default function SeasonIcon({ season, size, style }: { season: string | null; size: number; style?: React.CSSProperties }) {
  if (!isSeason(season)) return null;
  return (
    <UI_SeasonIcon
      season={season}
      size={size}
      label={season === 'winter' ? 'Winter season' : 'Summer season'}
      style={{ color: season === 'winter' ? POSTER_COLOR.sky : POSTER_COLOR.gold, ...style }}
    />
  );
}
