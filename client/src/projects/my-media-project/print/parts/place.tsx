import React from 'react';
import UI_MedalIcon from '../../../components/mix/medal-icon/medal-icon';
import type { PrintPlace } from '../print-data';

export type PlaceCircleSize = 'medal-80' | 'medal-96' | 'medal-116';

/**
 * Место кружком — `UI_MedalIcon` в крупных ступенях картинки. Медальная заливка только там,
 * где медаль вручали; иначе номер без кружка (место ≠ медаль). Места нет — ничего.
 */
export function PlaceCircle({ place, size }: { place: PrintPlace | null; size: PlaceCircleSize }) {
  if (!place) return null;
  return (
    <UI_MedalIcon
      place={String(place.value)}
      styleType={place.medal ? 'icon-place' : 'icon-noplace'}
      styleSize={size}
    />
  );
}

const RIBBON_BAND = '#e2452b';

const RIBBON_MEDAL: Record<number, { disc: string; core: string; label: string }> = {
  1: { disc: '#f2b632', core: '#ffd978', label: 'Gold medal' },
  2: { disc: '#b3bdc8', core: '#e4e9ee', label: 'Silver medal' },
  3: { disc: '#c98a4b', core: '#ebbf93', label: 'Bronze medal' },
};

/** Цвет медали места 1–3 — кружки счёта медалей на плакате. */
export const medalColor = (place: 1 | 2 | 3): string => RIBBON_MEDAL[place].disc;

/** Медаль с лентой заданной ширины (пропорция 28×60). Медали не вручали — ничего. */
export function RibbonMedal({ place, width }: { place: PrintPlace | null; width: number }) {
  const medal = place?.medal ? RIBBON_MEDAL[place.value] : undefined;
  if (!medal) return null;
  return (
    <svg width={width} height={Math.round((width * 60) / 28)} viewBox="0 0 28 60" role="img" aria-label={medal.label} style={{ flex: 'none' }}>
      <path d="M9 0h10v30H9z" fill={RIBBON_BAND} />
      <circle cx="14" cy="44" r="13" fill={medal.disc} />
      <circle cx="14" cy="44" r="8" fill={medal.core} />
    </svg>
  );
}
