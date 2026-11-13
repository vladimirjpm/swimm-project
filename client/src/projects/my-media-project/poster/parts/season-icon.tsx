import React from 'react';
import UI_SeasonIcon, { isSeason } from '../../../components/mix/season-icon/season-icon';
import { PRINT_SEASON_SCALE } from '../../print/print-theme';

/**
 * Крупный значок сезона рядом с годом (раскладка C) — общий `UI_SeasonIcon` (эмодзи).
 * `size` — размер макета; значок рисуется крупнее на общий множитель `PRINT_SEASON_SCALE`.
 * Сезон неизвестен — ничего.
 */
export default function SeasonIcon({ season, size, style }: { season: string | null; size: number; style?: React.CSSProperties }) {
  if (!isSeason(season)) return null;
  return <UI_SeasonIcon season={season} size={size * PRINT_SEASON_SCALE} label={season === 'winter' ? 'Winter season' : 'Summer season'} style={style} />;
}
