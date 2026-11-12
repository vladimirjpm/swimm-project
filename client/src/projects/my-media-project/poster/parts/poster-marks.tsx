import React from 'react';
import { PlaceCircle, RibbonMedal } from '../../print/parts/place';
import type { PrintPlace } from '../../print/print-data';
import { PRINT_FONT } from '../../print/print-fonts';
import type { PosterBadge } from '../poster-data';
import { POSTER_BADGE_COLOR, POSTER_COLOR } from '../poster-theme';

/** Метка строки (`REC`, `SB`, `PB` или словами) в цветах макета. */
export function BadgeChip({ badge, text, size, padding, radius }: {
  badge: PosterBadge;
  text: string;
  size: number;
  padding: string;
  radius: number;
}) {
  return (
    <span style={{ ...POSTER_BADGE_COLOR[badge.kind], padding, borderRadius: radius, fontSize: size, fontWeight: 800, lineHeight: 1, whiteSpace: 'nowrap' }}>
      {text}
    </span>
  );
}

/** Номер места без медали — приглушённым числом (место ≠ медаль). */
function PlaceNumber({ value, size }: { value: number; size: number }) {
  return (
    <span style={{ fontFamily: PRINT_FONT.digits, fontSize: size, fontWeight: 900, lineHeight: 1, color: POSTER_COLOR.muted }}>
      {value}
    </span>
  );
}

/** Место в строке раскладок A и C: медаль с лентой, без медали — число, без места — пусто. */
export function RibbonPlace({ place, width, numberSize }: { place: PrintPlace | null; width: number; numberSize: number }) {
  if (!place) return null;
  return place.medal ? <RibbonMedal place={place} width={width} /> : <PlaceNumber value={place.value} size={numberSize} />;
}

/** Сторона кружка `UI_MedalIcon` ступени `medal-96`. */
const CIRCLE = 96;

/**
 * Место в таблице раскладки B: кружок `UI_MedalIcon` с медальной заливкой, без медали — число.
 * У компонента ступени размера фиксированные, поэтому под масштаб строки кружок ужимается
 * transform-ом в рамке нужного размера.
 */
export function CirclePlace({ place, scale }: { place: PrintPlace | null; scale: number }) {
  const side = CIRCLE * scale;
  return (
    <span style={{ width: side, height: side, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      {place && (place.medal ? (
        <span style={{ width: side, height: side }}>
          <span style={{ display: 'block', width: CIRCLE, height: CIRCLE, transform: `scale(${scale})`, transformOrigin: '0 0' }}>
            <PlaceCircle place={place} size="medal-96" />
          </span>
        </span>
      ) : <PlaceNumber value={place.value} size={52 * scale} />)}
    </span>
  );
}
