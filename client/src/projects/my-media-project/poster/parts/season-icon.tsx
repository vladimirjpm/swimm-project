import React from 'react';
import { POSTER_COLOR } from '../poster-theme';

/** Снежинка — рисунок макета `PosterC`. */
const WINTER = 'M24 4v40M6.7 14l34.6 20M41.3 14L6.7 34M19 8l5 5 5-5M19 40l5-5 5 5M8 20.5l6.8-1.8L13 12'
  + 'M40 27.5l-6.8 1.8L35 36M40 20.5l-6.8-1.8L35 12M8 27.5l6.8 1.8L13 36';
/** Солнце — в макете летнего варианта нет, нарисовано в той же линии. */
const SUMMER = 'M24 4v6M24 38v6M4 24h6M38 24h6M9.9 9.9l4.2 4.2M33.9 33.9l4.2 4.2M9.9 38.1l4.2-4.2M33.9 14.1l4.2-4.2';

/**
 * Крупный значок сезона (раскладка C). Эмодзи плитки сюда не годится: у каждой системы оно
 * своё, а на плакате значок 140px. Сезон неизвестен — ничего.
 */
export default function SeasonIcon({ season, size, style }: { season: string | null; size: number; style?: React.CSSProperties }) {
  if (!season) return null;
  const winter = season === '❄️';
  return (
    <svg
      width={size} height={size} viewBox="0 0 48 48" fill="none" stroke={winter ? POSTER_COLOR.sky : POSTER_COLOR.gold}
      strokeWidth="3" strokeLinecap="round" role="img" aria-label={winter ? 'Winter season' : 'Summer season'} style={style}
    >
      <path d={winter ? WINTER : SUMMER} />
      {!winter && <circle cx="24" cy="24" r="9" />}
    </svg>
  );
}
