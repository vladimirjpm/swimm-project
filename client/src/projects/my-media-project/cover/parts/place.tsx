import React from 'react';
import UI_MedalIcon from '../../../components/mix/medal-icon/medal-icon';
import type { CoverPlace } from '../cover-data';

/**
 * Место кружком — `UI_MedalIcon` в ступенях обложки. Медальная заливка только там, где медаль
 * вручали; иначе номер без кружка (место ≠ медаль). Места нет — ничего.
 */
export function PlaceCircle({ place, size }: { place: CoverPlace | null; size: 'medal-80' | 'medal-116' }) {
  if (!place) return null;
  return (
    <UI_MedalIcon
      place={String(place.value)}
      styleType={place.medal ? 'icon-place' : 'icon-noplace'}
      styleSize={size}
    />
  );
}

const RIBBON = { width: 40, height: 86, band: '#e2452b' } as const;

const RIBBON_MEDAL: Record<number, { disc: string; core: string; label: string }> = {
  1: { disc: '#f2b632', core: '#ffd978', label: 'Gold medal' },
  2: { disc: '#b3bdc8', core: '#e4e9ee', label: 'Silver medal' },
  3: { disc: '#c98a4b', core: '#ebbf93', label: 'Bronze medal' },
};

/** Медаль с лентой (раскладка C). Без медали — номер места без заливки, как в строке списка. */
export function PlaceRibbon({ place }: { place: CoverPlace | null }) {
  if (!place) return null;
  const medal = place.medal ? RIBBON_MEDAL[place.value] : undefined;
  if (!medal) return <PlaceCircle place={place} size="medal-80" />;
  return (
    <svg width={RIBBON.width} height={RIBBON.height} viewBox="0 0 28 60" role="img" aria-label={medal.label} style={{ flex: 'none' }}>
      <path d="M9 0h10v30H9z" fill={RIBBON.band} />
      <circle cx="14" cy="44" r="13" fill={medal.disc} />
      <circle cx="14" cy="44" r="8" fill={medal.core} />
    </svg>
  );
}
