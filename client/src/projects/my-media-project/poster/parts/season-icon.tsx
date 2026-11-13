import React from 'react';
import UI_SeasonIcon, { isSeason } from '../../../components/mix/season-icon/season-icon';

/**
 * Крупный значок сезона рядом с годом (раскладка C) — общий `UI_SeasonIcon` (эмодзи).
 * Сезон неизвестен — ничего.
 */
export default function SeasonIcon({ season, size, style }: { season: string | null; size: number; style?: React.CSSProperties }) {
  if (!isSeason(season)) return null;
  return <UI_SeasonIcon season={season} size={size} label={season === 'winter' ? 'Winter season' : 'Summer season'} style={style} />;
}
